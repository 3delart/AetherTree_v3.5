# Système de déplacement des skills — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter un déplacement composable (`DisplacementType`: DashSelf/TeleportSelf/Pull/Push/
SwapPosition) aux skills, en absorbant `Dash_Target`/`Dash_Direction` (TargetType) et l'ancien
système `SkillSpecialEffect` de déplacement (Pull/Push/SwapPosition/PullAoE/PushAoE/GatherAoE/
Vortex/TeleportSelf/TeleportTarget), aucun des deux composable avec les dégâts/buffs/debuffs
normaux du skill.

**Architecture:** Nouveau champ `SkillData.displacementType`, indépendant de `targetType`/
`effectType`, exactement comme `isTrajectory`/`hasDelayedImpact` déjà en place. Nouveau point
d'entrée `SkillSystem.StartDisplacement()` qui bypass `DispatchByTargetType()` (même principe que
`StartTrajectory()`/`PlantDelayedZone()`), vérifié EN PREMIER aux 4 points d'entrée existants
(`SkillBar`×2, `CombatAIController`×2). Réutilise `PassesAoeFilter`/`OverlapSphere` (sélection de
zone), le filtre angulaire de `ExecuteCone()` (colonnes Cone), `NavMesh.SamplePosition` (clamp de
destination, pattern déjà dans `DashInDirection()`), et le système de résistance aux debuffs déjà
fonctionnel (`DebuffResistanceEntry`/`CharacterStats.ApplyDebuffResistances()`).

**Tech Stack:** Unity C#, pas de framework de test automatisé — vérification par compilation +
grep + Play Mode manuel (Florian).

**Spec:** `docs/superpowers/specs/2026-09-22-skill-displacement-design.md` — lire en entier avant
de commencer, notamment §2bis (intégration dispatch) et §2ter (suppression de l'ancien système
`SkillSpecialEffect`, trouvé en re-relecture, pas dans le brainstorming initial).

## Global Constraints

- Aucun framework de test automatisé — chaque tâche se vérifie par compilation Unity (0 erreur) +
  `grep` ciblé, jamais par une suite de tests.
- Repo root réel = `c:\AetherTree_v3.5\Assets\Scripts` (le `.git` vit ici, pas à la racine
  `C:\AetherTree_v3.5`). Pas de worktree — commits directs sur `master`.
- Suppression pure de `TargetType.Dash_Target`/`Dash_Direction` ET de
  `SkillSpecialEffect.Pull/Push/SwapPosition/PullAoE/PushAoE/GatherAoE/Vortex/TeleportSelf/
  TeleportTarget` (pas de placeholder `[Obsolete]`) — **0 asset ne référence AUCUNE des deux séries
  de valeurs supprimées**, déjà vérifié deux fois (spec §2 et §2ter). Re-vérifier au moment de
  l'exécution (Tâche 1, Step 1) plutôt que de faire confiance aveuglément à la spec.
- `ShowIfAttribute` (`Utils/ShowIfAttribute.cs`) — `AllowMultiple = false`, UN SEUL `AndField` par
  champ. `isTrajectory`/`hasDelayedImpact` utilisent déjà leur slot pour `executionType ==
  Normal` — leur exclusion mutuelle avec `displacementType` est donc un warning `OnValidate`
  SEULEMENT, jamais un `[ShowIf]` supplémentaire (spec §2bis, corrigé après vérification directe
  de l'attribut).
- `DisplacementUtils.WarpToNavMesh` (`Combat/DisplacementUtils.cs`) est RÉUTILISÉ tel quel pour
  les verbes instantanés (`TeleportSelf`, `SwapPosition`). `WarpEntity`/`ApplyDisplacementAoE`/
  `GatherAoE` (mêmes fichier) deviennent du code mort une fois l'ancien système supprimé (Tâche
  1) — supprimés en Tâche 1 également, pas réutilisés (pas de filtre `aoeFaction`, comportement
  volontairement abandonné, voir spec §2ter).
- Ordinal safety normale ailleurs : `DebuffType.Displacement` est un AJOUT en fin d'enum (jamais
  de suppression sur cet enum dans ce chantier).

---

### Task 1: `Data/Skills/SkillData.cs` — champs, absorption TargetType, suppression ancien système

**Files:**
- Modify: `Data/Skills/SkillData.cs`

**Interfaces:**
- Produces: `enum DisplacementType { None=0, DashSelf=1, TeleportSelf=2, Pull=3, Push=4,
  SwapPosition=5 }`, champs publics `DisplacementType displacementType`, `float
  displacementDistance`, `bool teleportBehindTarget`, `bool bringsAllies` sur `SkillData`.
  `enum TargetType { Target=0, Self=1, AoE_Self=2, AoE_Target=3, GroundTarget=4, Cone=5 }`
  (`Dash_Target`/`Dash_Direction` supprimés). `enum SkillSpecialEffect` réduit à `None=0,
  DrainHP=1, DrainMana=2, Summon=3, Interrupt=4` (Pull/Push/SwapPosition/PullAoE/PushAoE/
  GatherAoE/Vortex/TeleportSelf/TeleportTarget supprimés, `pullPushForce` supprimé). Consommés
  par les Tâches 3-4.
- Consumes: rien (première tâche du plan).

⚠ **Ce fichier seul ne compile pas après cette tâche** — `Combat/SkillSystem.cs` référence encore
`TargetType.Dash_Target`/`.Dash_Direction`, `SkillSpecialEffect.Pull` etc., et `pullPushForce`
jusqu'à la Tâche 3. Ne pas essayer de compiler seul après cette tâche — attendu, pas une erreur à
corriger ici.

- [ ] **Step 1: Re-vérifier qu'aucun `.asset` ne référence les valeurs à supprimer**

Depuis `c:\AetherTree_v3.5` (PAS `Assets/Scripts`) :

```bash
grep -rl "targetType: 6$\|targetType: 7$" --include=*.asset .   # Dash_Target / Dash_Direction
grep -rl "specialEffect: [1-9]$" --include=*.asset .            # tout specialEffect non-None
```

Expected pour la 1ère commande : **1 seule sortie**, `Content/Entity/Mobs/Wolf_test/
skl_loup_special_test.asset` (`targetType: 6`, Dash_Target — migré en Tâche 6, PAS un blocage ici).
Expected pour la 2ème commande : **aucune sortie**. Si la 2ème commande remonte quoi que ce soit,
ou si la 1ère remonte autre chose que ce seul fichier, STOP, ne pas continuer cette tâche, remonter
le cas au contrôleur d'abord.

- [ ] **Step 2: Renuméroter l'enum `TargetType`**

Remplacer :

```csharp
public enum TargetType
{
    Target = 0, Self = 1, AoE_Self = 2, AoE_Target = 3,
    GroundTarget = 4, Cone = 5, Dash_Target = 6, Dash_Direction = 7,
}
```

par :

```csharp
public enum TargetType
{
    Target = 0, Self = 1, AoE_Self = 2, AoE_Target = 3, GroundTarget = 4, Cone = 5,
}
```

- [ ] **Step 3: Réduire l'enum `SkillSpecialEffect` — supprimer les 9 valeurs de déplacement,
      renuméroter les 4 restantes**

Remplacer :

```csharp
public enum SkillSpecialEffect
{
    None           = 0,  // Pas d'effet spécial (valeur par défaut)

    // ── Déplacement cible unique ──────────────────────────────
    Pull           = 1,  // Attire la cible vers le caster
    Push           = 2,  // Repousse la cible loin du caster
    SwapPosition   = 3,  // Échange la position caster ↔ cible

    // ── Déplacement zone ──────────────────────────────────────
    PullAoE        = 4,  // Attire toutes les entités de la zone vers le caster
    PushAoE        = 5,  // Repousse toutes les entités de la zone
    GatherAoE      = 6,  // Regroupe toutes les entités vers le centre de la zone
    Vortex         = 7,  // Attire en spirale vers un point (= GatherAoE + Slow)

    // ── Téléportation ─────────────────────────────────────────
    TeleportSelf   = 8,  // Téléporte le caster vers la cible / point au sol
    TeleportTarget = 9,  // Téléporte la cible vers le caster

    // ── Drain / Transfert ─────────────────────────────────────
    DrainHP        = 10, // Vol de HP : dégâts sur cible → soin caster (drainHealRatio)
    DrainMana      = 11, // Vol de Mana : vide la cible, rend le caster

    // ── Invocation ────────────────────────────────────────────
    Summon         = 12, // Invoque un mob allié (summonMobData) — TODO phase suivante

    // ── Divers ────────────────────────────────────────────────
    Interrupt      = 13, // Annule le cast en cours de la cible — TODO phase suivante
}
```

par :

```csharp
public enum SkillSpecialEffect
{
    None           = 0,  // Pas d'effet spécial (valeur par défaut)

    // ── Drain / Transfert ─────────────────────────────────────
    DrainHP        = 1, // Vol de HP : dégâts sur cible → soin caster (drainHealRatio)
    DrainMana      = 2, // Vol de Mana : vide la cible, rend le caster

    // ── Invocation ────────────────────────────────────────────
    Summon         = 3, // Invoque un mob allié (summonMobData) — TODO phase suivante

    // ── Divers ────────────────────────────────────────────────
    Interrupt      = 4, // Annule le cast en cours de la cible — TODO phase suivante

    // Pull/Push/SwapPosition/PullAoE/PushAoE/GatherAoE/Vortex/TeleportSelf/TeleportTarget
    // (ordinaux 1-9) supprimés — absorbés par SkillData.DisplacementType, composable avec
    // n'importe quel effectType (Damage/Buff/Debuff/Other), contrairement à specialEffect qui
    // n'existe que si effectType = Other. 0 asset ne les utilisait (vérifié Step 1).
}
```

- [ ] **Step 4: Supprimer le champ `pullPushForce`**

Remplacer :

```csharp
    [Tooltip("Force du déplacement pour Pull/Push/PullAoE/PushAoE/GatherAoE/Vortex.\nDistance en unités world.")]
    [ShowIf(nameof(specialEffect), SkillSpecialEffect.Pull, SkillSpecialEffect.Push, SkillSpecialEffect.PullAoE, SkillSpecialEffect.PushAoE, SkillSpecialEffect.GatherAoE, SkillSpecialEffect.Vortex)]
    public float pullPushForce = 5f;

    [Tooltip("Ratio des dégâts restitués en soin (DrainHP). Ex: 0.5 = 50% des dégâts soignés.")]
```

par :

```csharp
    [Tooltip("Ratio des dégâts restitués en soin (DrainHP). Ex: 0.5 = 50% des dégâts soignés.")]
```

- [ ] **Step 5: Nettoyer le `[ShowIf]` de `aoeFaction`**

Remplacer :

```csharp
    [ShowIf(nameof(targetType), TargetType.AoE_Self, TargetType.AoE_Target, TargetType.GroundTarget,
        TargetType.Cone, TargetType.Target, TargetType.Dash_Target, DisplayName = "Cible")]
    public SkillAoeFaction aoeFaction = SkillAoeFaction.Enemies;
```

par :

```csharp
    [ShowIf(nameof(targetType), TargetType.AoE_Self, TargetType.AoE_Target, TargetType.GroundTarget,
        TargetType.Cone, TargetType.Target, DisplayName = "Cible")]
    public SkillAoeFaction aoeFaction = SkillAoeFaction.Enemies;
```

- [ ] **Step 6: Ajouter le bloc `DisplacementType` + 4 champs, juste après `aoeFaction`, avant le
      commentaire `── ⑦ Zone à impact différé ...`**

Trouver ce point exact (juste après le Step 5 ci-dessus) :

```csharp
    public SkillAoeFaction aoeFaction = SkillAoeFaction.Enemies;

    // ── ⑦ Zone à impact différé / Trajectoire mobile ───────────
```

Remplacer par :

```csharp
    public SkillAoeFaction aoeFaction = SkillAoeFaction.Enemies;

    // ── ⑥ Déplacement — composable avec effectType (Damage/Buff/Debuff/Other), CONTRAIREMENT à
    // l'ancien specialEffect qui n'existait que si effectType = Other (impossible de combiner
    // "fonce dans le tas" = déplacement + dégâts). Voir DisplacementType ci-dessous.
    [Tooltip("Aucun (défaut) | DashSelf (le caster fonce, trajet animé ~0.25-0.3s, dégâts aux " +
             "entités croisées) | TeleportSelf (le caster se téléporte, instantané) | Pull " +
             "(attire une/des cible(s) vers le caster ou un point) | Push (repousse une/des " +
             "cible(s) loin d'une origine) | SwapPosition (caster et cible échangent leurs " +
             "places, instantané).\n" +
             "Mutuellement exclusif avec isTrajectory/hasDelayedImpact (deux mécanismes de " +
             "\"quelque chose se déplace\" concurrents) — voir OnValidate, avertissement " +
             "seulement (isTrajectory/hasDelayedImpact utilisent déjà leur unique AndField pour " +
             "executionType, pas de ShowIf possible en plus).\n" +
             "Chaque verbe ne supporte qu'un sous-ensemble de targetType (voir guide de création " +
             "de skills pour la matrice complète) — combinaison non supportée = warning " +
             "OnValidate, pas de correction automatique.")]
    [Header("⑥ Déplacement")]
    public DisplacementType displacementType = DisplacementType.None;

    [Tooltip("Distance de déplacement RÉELLE — distincte de range (qui gate la portée de " +
             "CIBLAGE, jusqu'où tu peux cliquer/viser). Plafonne jusqu'où tu voyages/pousses " +
             "réellement dans cette direction, même si le point ciblé est plus loin (permet un " +
             "dash \"court\" même en visant loin).\n" +
             "Utilisée par DashSelf/TeleportSelf + Cone (direction souris) et GroundTarget " +
             "(plafond si le point cliqué est plus loin) ; ignorée pour Target (utilise l'offset " +
             "d'arrêt existant, pas de plafond). Toujours utilisée par Push. Jamais par Pull (sa " +
             "destination est toujours un point déjà déterminé : la position du caster ou le " +
             "point cliqué).")]
    [ShowIf(nameof(displacementType), DisplacementType.DashSelf, DisplacementType.TeleportSelf,
        DisplacementType.Push, Header = "⑥ Distance de déplacement")]
    public float displacementDistance = 5f;

    [Tooltip("TeleportSelf + targetType=Target uniquement : coché = atterrit DERRIÈRE la cible " +
             "(côté vers lequel elle tourne le dos — blink-backstab), décoché = DEVANT elle " +
             "(côté qu'elle regarde). Calculé par rapport au FACING DE LA CIBLE, pas du caster. " +
             "Toujours à côté, jamais SUR la cible. Choix figé par skill (décision designer), " +
             "pas calculé dynamiquement.")]
    [ShowIf(nameof(displacementType), DisplacementType.TeleportSelf,
        AndField = nameof(targetType), AndValue = TargetType.Target,
        Header = "⑥ Téléportation derrière la cible")]
    public bool teleportBehindTarget = true;

    [Tooltip("TeleportSelf uniquement : coché = les alliés dans aoeRadius de la position ORIGINE " +
             "du caster sont téléportés au même décalage relatif près de la destination. " +
             "Décoché (défaut) = caster seul.")]
    [ShowIf(nameof(displacementType), DisplacementType.TeleportSelf, Header = "⑥ Emmène les alliés")]
    public bool bringsAllies = false;

    // ── ⑦ Zone à impact différé / Trajectoire mobile ───────────
```

- [ ] **Step 7: Ajouter le warning `OnValidate` pour le combo interdit `displacementType` +
      `isTrajectory`/`hasDelayedImpact`**

Trouver la fin du bloc `OnValidate()` (juste avant l'accolade fermante `}` qui clôt la méthode,
après le dernier warning existant `isTrajectory = true avec executionType = ...`) :

```csharp
        if (isTrajectory && executionType != SkillExecutionType.Normal)
            Debug.LogWarning($"[SkillData:{name}] isTrajectory = true avec executionType = " +
                              $"{executionType} — combinaison non gérée, la trajectoire ne " +
                              "fonctionne qu'avec executionType = Normal (instant ou canalisé).", this);
    }
#endif
```

Remplacer par :

```csharp
        if (isTrajectory && executionType != SkillExecutionType.Normal)
            Debug.LogWarning($"[SkillData:{name}] isTrajectory = true avec executionType = " +
                              $"{executionType} — combinaison non gérée, la trajectoire ne " +
                              "fonctionne qu'avec executionType = Normal (instant ou canalisé).", this);

        // displacementType et isTrajectory/hasDelayedImpact sont deux mécanismes de "quelque
        // chose se déplace" concurrents — jamais de sens de les cumuler. Pas de [ShowIf] possible
        // ici (isTrajectory/hasDelayedImpact utilisent déjà leur unique AndField pour
        // executionType == Normal, voir Utils/ShowIfAttribute.cs — AllowMultiple = false),
        // avertissement seulement.
        if (displacementType != DisplacementType.None && (isTrajectory || hasDelayedImpact))
            Debug.LogWarning($"[SkillData:{name}] displacementType = {displacementType} avec " +
                              $"isTrajectory = {isTrajectory} et/ou hasDelayedImpact = " +
                              $"{hasDelayedImpact} — combinaison non supportée, deux mécanismes " +
                              "de déplacement concurrents sur le même skill. Décoche isTrajectory/" +
                              "hasDelayedImpact ou remets displacementType sur None.", this);

        // Combos targetType non supportés par verbe (voir matrice complète dans le guide de
        // création de skills) — même idiome que les warnings hasDelayedImpact/isTrajectory
        // ci-dessus : avertissement seulement, pas de correction automatique. displacementType
        // masqué dans l'Inspector uniquement par targetType == Target pour SwapPosition (voir
        // Step 6) ; les autres combos non supportés restent silencieusement actifs sans ce
        // check.
        bool displacementSupportedTargetType = displacementType switch
        {
            DisplacementType.None         => true,
            DisplacementType.DashSelf     => targetType == TargetType.Target
                                           || targetType == TargetType.GroundTarget
                                           || targetType == TargetType.Cone,
            DisplacementType.TeleportSelf => targetType == TargetType.Target
                                           || targetType == TargetType.GroundTarget
                                           || targetType == TargetType.Cone,
            DisplacementType.Pull         => targetType == TargetType.Target
                                           || targetType == TargetType.GroundTarget
                                           || targetType == TargetType.Cone
                                           || targetType == TargetType.AoE_Self,
            DisplacementType.Push         => targetType == TargetType.Target
                                           || targetType == TargetType.GroundTarget
                                           || targetType == TargetType.Cone
                                           || targetType == TargetType.AoE_Self,
            DisplacementType.SwapPosition => targetType == TargetType.Target,
            _                              => true,
        };
        if (!displacementSupportedTargetType)
            Debug.LogWarning($"[SkillData:{name}] displacementType = {displacementType} avec " +
                              $"targetType = {targetType} — combinaison non supportée pour ce " +
                              "verbe (voir la matrice verbe×targetType dans le guide de création " +
                              "de skills). Le déplacement se lancera quand même au runtime, " +
                              "traité avec un fallback générique potentiellement pas celui " +
                              "voulu.", this);
    }
#endif
```

- [ ] **Step 8: Compiler**

Ne compile PAS encore (Tâches 2-4 restantes) — c'est attendu. Vérifier juste qu'aucune ERREUR
n'apparaît en dehors de celles attribuables à `Combat/SkillSystem.cs` (le seul fichier qui
référence encore les valeurs supprimées à ce stade).

- [ ] **Step 9: Commit**

```bash
git add Data/Skills/SkillData.cs
git commit -m "refactor: SkillData — DisplacementType field, absorb Dash_Target/Direction, drop old SkillSpecialEffect displacement

New composable DisplacementType (DashSelf/TeleportSelf/Pull/Push/
SwapPosition), independent of effectType/targetType. TargetType drops
Dash_Target/Dash_Direction (absorbed). SkillSpecialEffect drops its 9
displacement values + pullPushForce field (0 assets used any of it,
DisplacementUtils.WarpToNavMesh reused for instant verbs in the next
task, WarpEntity/ApplyDisplacementAoE/GatherAoE become dead code, no
aoeFaction filter to preserve). New OnValidate warnings for the
displacementType+isTrajectory/hasDelayedImpact conflict and
unsupported verb+targetType combos.

NOTE: this commit alone does not compile — SkillSystem.cs still
references the removed enum values, fixed in the next commit."
```

---

### Task 2: `Data/StatusEffect/StatusEffectData.cs` — `DebuffType.Displacement`

**Files:**
- Modify: `Data/StatusEffect/StatusEffectData.cs`

**Interfaces:**
- Produces: `DebuffType.Displacement = 22`, consommé par la Tâche 3 (check de résistance).
- Consumes: rien (indépendant de la Tâche 1).

- [ ] **Step 1: Ajouter `Displacement` en fin d'enum `DebuffType`**

Trouver :

```csharp
    HpDrain    = 21, // Vol de vie progressif — dégâts/s sur la cible (TakeDamage, respecte
                // défense/résistances) reversés en soin au lanceur du debuff (§3.1.1.1)
}
```

Remplacer par :

```csharp
    HpDrain    = 21, // Vol de vie progressif — dégâts/s sur la cible (TakeDamage, respecte
                // défense/résistances) reversés en soin au lanceur du debuff (§3.1.1.1)

    Displacement = 22, // Résistance au déplacement forcé — Pull, Push ET SwapPosition
                // (SkillData.DisplacementType) partagent cette même clé, pas de distinction par
                // verbe. Clé de résistance PURE, aucun DebuffData n'utilise jamais ce type,
                // jamais appliqué comme un vrai debuff (pas de durée, pas de
                // statusEffects.isXxx) — juste un point d'entrée dans le système de résistance
                // équipement/Mob/PNJ existant (voir SkillSystem.StartDisplacement,
                // MobData/PNJData.debuffResistances).
}
```

- [ ] **Step 2: Compiler**

Doit compiler tel quel — cette tâche est totalement indépendante des Tâches 1/3/4 (nouvel ordinal
en fin d'enum, aucune référence ailleurs pour l'instant).

- [ ] **Step 3: Commit**

```bash
git add Data/StatusEffect/StatusEffectData.cs
git commit -m "feat: add DebuffType.Displacement resistance key

Pure resistance key for forced displacement (Pull/Push/SwapPosition) -
no DebuffData ever uses it, reuses the existing equipment/Mob/PNJ
resistance pipeline end to end instead of building a parallel system."
```

---

### Task 3: `Combat/SkillSystem.cs` — `StartDisplacement()`, suppression ancien système

**Files:**
- Modify: `Combat/SkillSystem.cs`

**Interfaces:**
- Consumes: `DisplacementType`/`displacementDistance`/`teleportBehindTarget`/`bringsAllies`
  (Tâche 1), `DebuffType.Displacement` (Tâche 2).
- Produces: `public void StartDisplacement(SkillData skill, Entity caster, Entity target)` —
  consommée par la Tâche 4 (`SkillBar.cs`/`CombatAIController.cs`).

⚠ Ce fichier + Tâches 1-2 compilent ENSEMBLE seulement après la Tâche 4
(`SkillBar.cs`/`CombatAIController.cs` référencent aussi `TargetType.Dash_Target`/
`.Dash_Direction` jusque-là).

- [ ] **Step 1: Mettre à jour le bloc de commentaire en tête de fichier**

Remplacer :

```csharp
// Target nullable par design :
//   Self / AoE_Self / GroundTarget / Cone → target null autorisée
//   Target / AoE_Target / Dash_Target      → target requise
//
// Points d'entrée :
//   Execute(skill, caster, target)
//     → Enregistrements Player (UseSkill, SkillUsedEvent)
//     → RegisterLastSkill sur Mob cible
//     → Dispatch selon executionType (MultiHit / Standard)
//     → Dispatch selon skill.targetType
//     → ApplyEffectType → CombatSystem (calcul pur)
//     → GameEventBus.Publish(DamageDealtEvent) [Player uniquement]
//
// TargetType gérés :
//   Target        → monocible sur Entity requise. + isTrajectory : perce jusqu'à la cible
//                   (stopAtFirstHit pour s'arrêter au 1er hit).
//   AoE_Target    → zone autour de la cible
//   Dash_Target   → dash vers la cible puis dégâts
//   Self          → buff/heal sur le caster
//   AoE_Self      → zone autour du caster
//   GroundTarget  → zone autour d'un point au sol (SetGroundTargetPoint), visé souris.
//                   + isTrajectory : voyage vers ce point (stopAtFirstHit possible).
//   Cone          → cône en éventail devant le caster, visé souris (SetSkillDirection via
//                   TargetingSystem.ResolveDirection())
//   Dash_Direction → dash vers un point directionnel (SetSkillDirection ou forward)
//
// MultiHit (GDD §7.1) :
//   Hit initial = skill parent, puis chaque HitStep en coroutine.
//   Proxy SkillData temporaire — détruit après usage.
//
// GroundTarget :
//   SetGroundTargetPoint(point) appelé par TargetingSystem avant Execute().
//   Remis à null après utilisation.
//
// Cone / Dash_Direction :
//   SetSkillDirection(dir) appelé par SkillBar avant Execute()/StartTrajectory()
//   pour les skills directionnels. Remis à null après utilisation.
// =============================================================
```

par :

```csharp
// Target nullable par design :
//   Self / AoE_Self / GroundTarget / Cone → target null autorisée
//   Target / AoE_Target                    → target requise
//
// Points d'entrée :
//   Execute(skill, caster, target)
//     → Enregistrements Player (UseSkill, SkillUsedEvent)
//     → RegisterLastSkill sur Mob cible
//     → Dispatch selon executionType (MultiHit / Standard)
//     → Dispatch selon skill.targetType
//     → ApplyEffectType → CombatSystem (calcul pur)
//     → GameEventBus.Publish(DamageDealtEvent) [Player uniquement]
//
// TargetType gérés :
//   Target        → monocible sur Entity requise. + isTrajectory : perce jusqu'à la cible
//                   (stopAtFirstHit pour s'arrêter au 1er hit).
//   AoE_Target    → zone autour de la cible
//   Self          → buff/heal sur le caster
//   AoE_Self      → zone autour du caster
//   GroundTarget  → zone autour d'un point au sol (SetGroundTargetPoint), visé souris.
//                   + isTrajectory : voyage vers ce point (stopAtFirstHit possible).
//   Cone          → cône en éventail devant le caster, visé souris (SetSkillDirection via
//                   TargetingSystem.ResolveDirection())
//
// StartDisplacement(skill, caster, target) — point d'entrée séparé, bypass complet de
// DispatchByTargetType (même principe que StartTrajectory/PlantDelayedZone), pour tout skill
// displacementType != None (DashSelf/TeleportSelf/Pull/Push/SwapPosition — voir sa doc dédiée
// plus bas dans ce fichier). Absorbe l'ancien Dash_Target/Dash_Direction (TargetType) ET l'ancien
// SkillSpecialEffect de déplacement (Pull/Push/SwapPosition/PullAoE/PushAoE/GatherAoE/Vortex/
// TeleportSelf/TeleportTarget, tous deux supprimés).
//
// MultiHit (GDD §7.1) :
//   Hit initial = skill parent, puis chaque HitStep en coroutine.
//   Proxy SkillData temporaire — détruit après usage.
//
// GroundTarget :
//   SetGroundTargetPoint(point) appelé par TargetingSystem avant Execute().
//   Remis à null après utilisation.
//
// Cone :
//   SetSkillDirection(dir) appelé par SkillBar avant Execute()/StartTrajectory()
//   pour les skills directionnels. Remis à null après utilisation.
// =============================================================
```

- [ ] **Step 2: `DispatchByTargetType()` — retirer les `case TargetType.Dash_Target`/`Dash_Direction`**

Remplacer (méthode complète) :

```csharp
    private void DispatchByTargetType(SkillData skill, Entity caster, Entity target)
    {
        switch (skill.targetType)
        {
            // ── Requièrent une target Entity ──────────────────
            case TargetType.Target:
                if (target != null && !target.isDead)
                    ExecuteOnTarget(skill, caster, target);
                else
                    LogMissingTarget(skill, caster);
                break;

            case TargetType.AoE_Target:
                if (target != null && !target.isDead)
                    ExecuteAoETarget(skill, caster, target);
                else
                    LogMissingTarget(skill, caster);
                break;

            case TargetType.Dash_Target:
                if (target != null && !target.isDead && PassesAoeFilter(skill.aoeFaction, caster, target))
                    StartCoroutine(DashToTarget(skill, caster, target));
                else
                    LogMissingTarget(skill, caster);
                break;

            // ── Pas de target Entity ──────────────────────────
            case TargetType.Self:
                ExecuteOnSelf(skill, caster);
                break;

            case TargetType.AoE_Self:
                ExecuteAoESelf(skill, caster);
                break;

            case TargetType.GroundTarget:
                ExecuteGroundTarget(skill, caster);
                break;

            case TargetType.Cone:
                ExecuteCone(skill, caster);
                break;

            case TargetType.Dash_Direction:
                StartCoroutine(DashInDirection(skill, caster));
                break;

            default:
                Debug.LogWarning($"[SKILL] TargetType '{skill.targetType}' non géré — {caster.entityName} / {skill.name}.");
                break;
        }
    }
```

par :

```csharp
    private void DispatchByTargetType(SkillData skill, Entity caster, Entity target)
    {
        switch (skill.targetType)
        {
            // ── Requièrent une target Entity ──────────────────
            case TargetType.Target:
                if (target != null && !target.isDead)
                    ExecuteOnTarget(skill, caster, target);
                else
                    LogMissingTarget(skill, caster);
                break;

            case TargetType.AoE_Target:
                if (target != null && !target.isDead)
                    ExecuteAoETarget(skill, caster, target);
                else
                    LogMissingTarget(skill, caster);
                break;

            // ── Pas de target Entity ──────────────────────────
            case TargetType.Self:
                ExecuteOnSelf(skill, caster);
                break;

            case TargetType.AoE_Self:
                ExecuteAoESelf(skill, caster);
                break;

            case TargetType.GroundTarget:
                ExecuteGroundTarget(skill, caster);
                break;

            case TargetType.Cone:
                ExecuteCone(skill, caster);
                break;

            default:
                Debug.LogWarning($"[SKILL] TargetType '{skill.targetType}' non géré — {caster.entityName} / {skill.name}.");
                break;
        }
    }
```

- [ ] **Step 3: Supprimer `DashToTarget()` et `DashInDirection()`**

Leur logique (Lerp, `agent.enabled`, `Mob.IsDashing`, sweep par frame) est le point de départ de
`DashSelfRoutine()` ajoutée au Step 8 — généralisée, pas dupliquée ici.

Remplacer (du commentaire `// DASH TARGET` jusqu'à la fin de `DashInDirection()`, juste avant le
commentaire `// FILTRE ALLIÉ/ENNEMI`) :

```csharp
    // =========================================================
    // DASH TARGET — le caster fonce vers la cible
    // =========================================================

    private IEnumerator DashToTarget(SkillData skill, Entity caster, Entity target)
    {
        float dashDuration = caster.entityType == EntityType.Player ? 0.3f : 0.25f;
        float stopOffset   = caster.entityType == EntityType.Player ? 1.5f : 1.2f;

        float   elapsed  = 0f;
        Vector3 startPos = caster.transform.position;
        Vector3 endPos   = target.transform.position
                         - (target.transform.position - startPos).normalized * stopOffset;

        var agent = caster.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null) agent.enabled = false;

        if (caster is Mob dashMob) dashMob.IsDashing = true;

        while (elapsed < dashDuration)
        {
            if (caster == null || caster.isDead) yield break;
            caster.transform.position = Vector3.Lerp(startPos, endPos, elapsed / dashDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (caster != null)
        {
            caster.transform.position = endPos;
            if (caster is Mob endMob) endMob.IsDashing = false;
            if (agent != null) agent.enabled = true;

            if (!caster.isDead && target != null && !target.isDead)
            {
                ApplyEffectType(skill, caster, target);
                ApplyStatusEffects(skill, caster, target);
                CheckKill(target);

                // vfxImpact/soundEffect joués ICI, au moment RÉEL du coup — Execute()/
                // ResolveExecute() ne les jouent plus pour Dash_Target (voir leur propre
                // commentaire), sinon ils partaient immédiatement au clic, ~0.3s avant l'arrivée
                // du dash (trouvé lors de l'audit VFX du 2026-09-12).
                if (skill.vfxImpact != null)
                    Instantiate(skill.vfxImpact, target.transform.position, Quaternion.identity);
                if (skill.soundEffect != null)
                    AudioSource.PlayClipAtPoint(skill.soundEffect, target.transform.position);
            }
        }
    }

    // =========================================================
    // DASH DIRECTION — dash en ligne droite dans une direction
    // Applique l'effet sur toutes les entités traversées.
    // =========================================================

    private IEnumerator DashInDirection(SkillData skill, Entity caster)
    {
        Vector3 dir = _skillDirection?.normalized ?? caster.transform.forward;
        _skillDirection = null;

        float dashDuration = caster.entityType == EntityType.Player ? 0.3f : 0.25f;
        float dashDistance = skill.range > 0f ? skill.range : 6f;

        Vector3 startPos = caster.transform.position;
        Vector3 endPos   = startPos + dir * dashDistance;

        // Vérifie si la destination est sur le NavMesh
        if (UnityEngine.AI.NavMesh.SamplePosition(endPos, out UnityEngine.AI.NavMeshHit navHit, dashDistance, UnityEngine.AI.NavMesh.AllAreas))
            endPos = navHit.position;

        var agent = caster.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null) agent.enabled = false;

        if (caster is Mob dashMob) dashMob.IsDashing = true;

        float   elapsed   = 0f;
        float   radius    = skill.aoeRadius > 0f ? skill.aoeRadius : 0.5f;
        var     alreadyHit = new HashSet<Entity>();

        while (elapsed < dashDuration)
        {
            if (caster == null || caster.isDead) yield break;

            Vector3 prev = caster.transform.position;
            caster.transform.position = Vector3.Lerp(startPos, endPos, elapsed / dashDuration);

            // Détecte les entités traversées frame par frame
            Collider[] cols = Physics.OverlapSphere(caster.transform.position, radius);
            foreach (Collider col in cols)
            {
                Entity entity = col.GetComponentInParent<Entity>();
                if (entity == null || entity == caster || entity.isDead) continue;
                if (alreadyHit.Contains(entity)) continue;

                alreadyHit.Add(entity);
                ApplyEffectType(skill, caster, entity);
                ApplyStatusEffects(skill, caster, entity);
                CheckKill(entity);

                // vfxImpact/soundEffect joués ICI, par entité touchée, au moment RÉEL du coup —
                // même raison que DashToTarget ci-dessus.
                if (skill.vfxImpact != null)
                    Instantiate(skill.vfxImpact, entity.transform.position, Quaternion.identity);
                if (skill.soundEffect != null)
                    AudioSource.PlayClipAtPoint(skill.soundEffect, entity.transform.position);
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (caster != null)
        {
            caster.transform.position = endPos;
            if (caster is Mob endMob) endMob.IsDashing = false;
            if (agent != null) agent.enabled = true;
        }
    }

    // =========================================================
    // FILTRE ALLIÉ/ENNEMI — effets de zone (SkillAoeFaction)
    // =========================================================
```

par (les 2 méthodes et leurs commentaires disparaissent, le commentaire `FILTRE ALLIÉ/ENNEMI`
se retrouve directement après `ExecuteCone()`) :

```csharp
    // =========================================================
    // FILTRE ALLIÉ/ENNEMI — effets de zone (SkillAoeFaction)
    // =========================================================
```

- [ ] **Step 4: `ApplySpecialEffect()` — supprimer les 9 `case` de déplacement, renuméroter les
      survivants**

Remplacer (du début de la méthode jusqu'au `case SkillSpecialEffect.DrainHP:` inclus) :

```csharp
    private void ApplySpecialEffect(SkillData skill, Entity caster, Entity target)
    {
        if (skill.specialEffect == SkillSpecialEffect.None)
        {
            Debug.LogWarning($"[SKILL] {skill.name} ({caster.entityName}) : effectType=Other mais specialEffect=None.");
            return;
        }

        Vector3 casterPos = caster.transform.position;

        switch (skill.specialEffect)
        {
            case SkillSpecialEffect.Pull:
                if (target == null) return;
                DisplacementUtils.WarpEntity(target, casterPos, skill.pullPushForce, towards: true);
                FloatingText.Spawn("PULL", target.transform.position, Color.yellow, 1.8f);
                break;

            case SkillSpecialEffect.Push:
                if (target == null) return;
                DisplacementUtils.WarpEntity(target, casterPos, skill.pullPushForce, towards: false);
                FloatingText.Spawn("PUSH", target.transform.position, Color.yellow, 1.8f);
                break;

            case SkillSpecialEffect.SwapPosition:
                if (target == null) return;
                Vector3 origCaster = casterPos;
                Vector3 origTarget = target.transform.position;
                DisplacementUtils.WarpToNavMesh(caster, origTarget);
                DisplacementUtils.WarpToNavMesh(target, origCaster);
                FloatingText.Spawn("SWAP", origTarget, Color.yellow, 1.8f);
                break;

            case SkillSpecialEffect.PullAoE:
            {
                Vector3 center = ResolveAoECenter(skill, caster);
                string  layer  = caster.entityType == EntityType.Mob ? "Mob" : "Player";
                int count = DisplacementUtils.ApplyDisplacementAoE(
                    center, skill.aoeRadius, skill.pullPushForce,
                    towardsCenter: true, caster: caster,
                    layerMask: ~LayerMask.GetMask(layer));
                FloatingText.Spawn($"PULL ×{count}", center, Color.yellow, 1.8f);
                break;
            }

            case SkillSpecialEffect.PushAoE:
            {
                Vector3 center = ResolveAoECenter(skill, caster);
                string  layer  = caster.entityType == EntityType.Mob ? "Mob" : "Player";
                int count = DisplacementUtils.ApplyDisplacementAoE(
                    center, skill.aoeRadius, skill.pullPushForce,
                    towardsCenter: false, caster: caster,
                    layerMask: ~LayerMask.GetMask(layer));
                FloatingText.Spawn($"PUSH ×{count}", center, Color.yellow, 1.8f);
                break;
            }

            case SkillSpecialEffect.GatherAoE:
            {
                Vector3 center = ResolveAoECenter(skill, caster);
                string  layer  = caster.entityType == EntityType.Mob ? "Mob" : "Player";
                int count = DisplacementUtils.GatherAoE(
                    center, skill.aoeRadius, caster,
                    layerMask: ~LayerMask.GetMask(layer));
                FloatingText.Spawn($"GATHER ×{count}", center, Color.magenta, 1.8f);
                break;
            }

            case SkillSpecialEffect.Vortex:
            {
                Vector3 center = ResolveAoECenter(skill, caster);
                string  layer  = caster.entityType == EntityType.Mob ? "Mob" : "Player";
                int count = DisplacementUtils.ApplyDisplacementAoE(
                    center, skill.aoeRadius, skill.pullPushForce,
                    towardsCenter: true, caster: caster,
                    layerMask: ~LayerMask.GetMask(layer));
                FloatingText.Spawn($"VORTEX ×{count}", center, Color.cyan, 1.8f);
                break;
            }

            case SkillSpecialEffect.TeleportSelf:
            {
                Vector3 dest = _groundTargetPoint ?? (target != null
                    ? target.transform.position
                    : casterPos);
                _groundTargetPoint = null;
                DisplacementUtils.WarpToNavMesh(caster, dest);
                FloatingText.Spawn("TELEPORT", dest, Color.cyan, 1.8f);
                break;
            }

            case SkillSpecialEffect.TeleportTarget:
                if (target == null) return;
                DisplacementUtils.WarpToNavMesh(target, casterPos);
                FloatingText.Spawn("TELEPORT", target.transform.position, Color.cyan, 1.8f);
                break;

            case SkillSpecialEffect.DrainHP:
```

par :

```csharp
    private void ApplySpecialEffect(SkillData skill, Entity caster, Entity target)
    {
        if (skill.specialEffect == SkillSpecialEffect.None)
        {
            Debug.LogWarning($"[SKILL] {skill.name} ({caster.entityName}) : effectType=Other mais specialEffect=None.");
            return;
        }

        switch (skill.specialEffect)
        {
            case SkillSpecialEffect.DrainHP:
```

Note : `Vector3 casterPos = caster.transform.position;` disparaît — plus aucun `case` restant
(`DrainHP`/`DrainMana`/`Summon`/`Interrupt`) ne le lit (vérifié en lisant leurs corps).

- [ ] **Step 5: Supprimer `ResolveAoECenter()` si elle n'est plus utilisée**

Chercher tous les appels restants à `ResolveAoECenter(` dans le fichier après le Step 4
ci-dessus. Si AUCUN appel ne reste (les seuls appelants étaient PullAoE/PushAoE/GatherAoE/Vortex,
tous supprimés), supprimer la méthode entière :

```csharp
    private Vector3 ResolveAoECenter(SkillData skill, Entity caster)
    {
        if (skill.targetType == TargetType.GroundTarget && _groundTargetPoint.HasValue)
            return _groundTargetPoint.Value;
        return caster.transform.position;
    }
```

Si un autre appelant existe ailleurs dans le fichier (vérifier avant de supprimer — cette méthode
est un utilitaire générique, pas garanti privé à `ApplySpecialEffect`), la garder et noter ce cas
dans le rapport de tâche plutôt que de supprimer aveuglément.

- [ ] **Step 6: Ajouter les 5 helpers partagés — coller juste après `PassesAoeFilter()`**

Trouver :

```csharp
    private static bool PassesAoeFilter(SkillAoeFaction filter, Entity caster, Entity other)
    {
        if (filter == SkillAoeFaction.Everyone) return true;
        bool ally = IsAlly(caster, other);
        return filter == SkillAoeFaction.Allies ? ally : !ally;
    }
```

Remplacer par (garde le bloc ci-dessus identique, ajoute les helpers juste après) :

```csharp
    private static bool PassesAoeFilter(SkillAoeFaction filter, Entity caster, Entity other)
    {
        if (filter == SkillAoeFaction.Everyone) return true;
        bool ally = IsAlly(caster, other);
        return filter == SkillAoeFaction.Allies ? ally : !ally;
    }

    // =========================================================
    // DÉPLACEMENT — helpers partagés par StartDisplacement() et ses 5 routines de verbe
    // =========================================================

    /// <summary>Résistance équipement/Mob/PNJ (DebuffType.Displacement, spec §7) — jet
    /// INDÉPENDANT par cible. True = résisté, aucun déplacement/dégât ne doit avoir lieu sur
    /// cette entité.</summary>
    private static bool ResistsDisplacement(Entity target)
    {
        if (target?.statusEffects == null) return false;
        float resistance = target.statusEffects.GetDebuffResistance(DebuffType.Displacement);
        return resistance > 0f && Random.value < resistance;
    }

    /// <summary>Clampe une destination sur le NavMesh sans déplacer quoi que ce soit — extrait de
    /// DisplacementUtils.WarpToNavMesh (qui warp directement, ne convient pas à un trajet animé
    /// frame par frame). Retourne false si aucun point valide n'est trouvé dans le rayon
    /// (l'appelant doit alors annuler le déplacement plutôt que d'utiliser une destination hors
    /// mesh).</summary>
    private static bool TryClampToNavMesh(Vector3 destination, out Vector3 clamped)
    {
        if (UnityEngine.AI.NavMesh.SamplePosition(destination, out var hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
        {
            clamped = hit.position;
            return true;
        }
        clamped = destination;
        return false;
    }

    /// <summary>Warp NavMesh-safe partagé par TeleportSelf/SwapPosition (verbes instantanés) —
    /// `destination` doit déjà être clampée via TryClampToNavMesh avant l'appel.</summary>
    private static void WarpNow(Entity entity, Vector3 destination)
    {
        var agent = entity.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.Warp(destination);
        else
            entity.transform.position = destination;
    }

    /// <summary>Sélectionne les entités dans un rayon autour d'un centre, filtrées aoeFaction,
    /// excluant systématiquement le caster (jamais de Pull/Push sur soi-même). Utilisée par
    /// GroundTarget/AoE_Self pour Pull/Push.</summary>
    private List<Entity> SelectZoneEntities(SkillData skill, Entity caster, Vector3 center, float radius)
    {
        var result = new List<Entity>();
        Collider[] hits = Physics.OverlapSphere(center, radius);
        foreach (Collider col in hits)
        {
            Entity entity = col.GetComponentInParent<Entity>();
            if (entity == null || entity == caster || entity.isDead) continue;
            if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;
            result.Add(entity);
        }
        return result;
    }

    /// <summary>Sélectionne les entités dans l'éventail devant le caster — même filtre angulaire
    /// exact que ExecuteCone(). Utilisée par Cone pour Pull/Push.</summary>
    private List<Entity> SelectConeEntities(SkillData skill, Entity caster, Vector3 dir)
    {
        var result = new List<Entity>();
        float range     = skill.range         > 0f ? skill.range         : 5f;
        float halfAngle = skill.coneHalfAngle > 0f ? skill.coneHalfAngle : 45f;
        Collider[] cols = Physics.OverlapSphere(caster.transform.position, range);
        foreach (Collider col in cols)
        {
            Entity entity = col.GetComponentInParent<Entity>();
            if (entity == null || entity == caster || entity.isDead) continue;
            if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;
            Vector3 toEntity = (entity.transform.position - caster.transform.position).normalized;
            if (Vector3.Angle(dir, toEntity) > halfAngle) continue;
            result.Add(entity);
        }
        return result;
    }

    /// <summary>Étale les entités d'un Pull en zone sur un petit cercle autour du point exact —
    /// évite la superposition visuelle si plusieurs entités atterrissent au même endroit (spec
    /// §6).</summary>
    private static Vector3 SpreadOffset(int index, int total, float radius = 0.6f)
    {
        if (total <= 1) return Vector3.zero;
        float angle = index * (360f / total) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
    }

    /// <summary>Hard CC — même set exact que le gate de SkillBar.TryUseSlot() (Stun/Shocked/
    /// Freeze/Knockback/Fear). Utilisé pour interrompre un DashSelf en cours (spec §5) — PAS
    /// pour bloquer un Pull/Push sur sa cible, qui s'applique toujours peu importe l'état de CC
    /// actuel de la cible (voir StartDisplacement, décision explicite).</summary>
    private static bool IsHardCCd(Entity entity)
    {
        var fx = entity.statusEffects;
        return fx != null && (fx.isStunned || fx.isShocked || fx.isFreezed || fx.isKnockedBack || fx.isFeared);
    }
```

- [ ] **Step 7: Ajouter `StartDisplacement()` — coller juste après `Execute()`/avant
      `ResolveExecute()`**

Remplacer :

```csharp
        // ── VFX & Son ────────────────────────────────────────
        if (!isDash)
        {
            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, vfxPos, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, caster.transform.position);
        }
    }

    /// <summary>Résout un skill DÉJÀ lancé par SkillBar (mana/anim/BeginSkillUse déjà faits au
```

par :

```csharp
        // ── VFX & Son ────────────────────────────────────────
        if (!isDash)
        {
            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, vfxPos, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, caster.transform.position);
        }
    }

    /// <summary>Point d'entrée unique pour tout skill displacementType != None (DashSelf/
    /// TeleportSelf/Pull/Push/SwapPosition) — bypass complet de DispatchByTargetType(), même
    /// principe que StartTrajectory()/PlantDelayedZone(). Bookkeeping Player identique à
    /// ResolveExecute() (skill DÉJÀ lancé par SkillBar/CombatAIController — mana/anim/
    /// BeginSkillUse déjà faits au lancement).</summary>
    public void StartDisplacement(SkillData skill, Entity caster, Entity target)
    {
        if (skill == null || caster == null || caster.isDead) return;

        if (caster.entityType == EntityType.Player && caster is Player player)
        {
            player.ResolveSkillUse(skill, target);

            GameEventBus.Publish(new SkillUsedEvent
            {
                skill          = skill,
                target         = target,
                caster         = player,
                primaryElement = skill.PrimaryElement,
                isCombo        = skill.elements != null && skill.elements.Count >= 2,
                locationID     = player.currentZoneID,
                isInParty      = false,
            });
        }

        if (target is Mob mobTarget && caster is Player attackerPlayer)
            mobTarget.RegisterLastSkill(attackerPlayer, skill);

        switch (skill.displacementType)
        {
            case DisplacementType.DashSelf:
                StartCoroutine(DashSelfRoutine(skill, caster, target));
                break;
            case DisplacementType.TeleportSelf:
                TeleportSelfNow(skill, caster, target);
                break;
            case DisplacementType.Pull:
                StartCoroutine(PullRoutine(skill, caster, target));
                break;
            case DisplacementType.Push:
                StartCoroutine(PushRoutine(skill, caster, target));
                break;
            case DisplacementType.SwapPosition:
                SwapPositionNow(skill, caster, target);
                break;
        }
    }

```

- [ ] **Step 8: Ajouter `DashSelfRoutine()` — juste avant le commentaire `FILTRE ALLIÉ/ENNEMI`
      laissé par le Step 3**

Remplacer :

```csharp
    // =========================================================
    // FILTRE ALLIÉ/ENNEMI — effets de zone (SkillAoeFaction)
    // =========================================================
```

par :

```csharp
    // =========================================================
    // DASH SELF — le caster fonce (Target/GroundTarget/Cone)
    // Target : dégâts UNIQUEMENT à l'arrivée sur la cible verrouillée (comportement DashToTarget
    // historique, inchangé). GroundTarget/Cone : sweep par frame, touche tout ce qui est croisé
    // (comportement DashInDirection historique, inchangé).
    // =========================================================

    private IEnumerator DashSelfRoutine(SkillData skill, Entity caster, Entity target)
    {
        float dashDuration = caster.entityType == EntityType.Player ? 0.3f : 0.25f;
        float stopOffset   = caster.entityType == EntityType.Player ? 1.5f : 1.2f;

        Vector3 startPos = caster.transform.position;
        Vector3 destination;
        bool    sweepEnRoute;

        if (skill.targetType == TargetType.Target)
        {
            if (target == null || target.isDead) { LogMissingTarget(skill, caster); yield break; }
            destination = target.transform.position
                        - (target.transform.position - startPos).normalized * stopOffset;
            sweepEnRoute = false;
        }
        else if (skill.targetType == TargetType.GroundTarget)
        {
            Vector3 point = _groundTargetPoint ?? startPos;
            _groundTargetPoint = null;
            Vector3 toPoint = point - startPos;
            float   dist    = toPoint.magnitude;
            destination = dist > skill.displacementDistance
                ? startPos + toPoint.normalized * skill.displacementDistance
                : point;
            sweepEnRoute = true;
        }
        else // Cone
        {
            Vector3 dir = _skillDirection?.normalized ?? caster.transform.forward;
            _skillDirection = null;
            destination = startPos + dir * skill.displacementDistance;
            sweepEnRoute = true;
        }

        if (!TryClampToNavMesh(destination, out destination)) yield break;

        var agent = caster.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null) agent.enabled = false;
        if (caster is Mob dashMob) dashMob.IsDashing = true;

        var   alreadyHit = new HashSet<Entity>();
        float radius     = skill.aoeRadius > 0f ? skill.aoeRadius : 0.5f;
        float elapsed    = 0f;
        bool  interrupted = false;

        while (elapsed < dashDuration)
        {
            if (caster == null || caster.isDead) yield break;
            if (IsHardCCd(caster)) { interrupted = true; break; }

            caster.transform.position = Vector3.Lerp(startPos, destination, elapsed / dashDuration);

            if (sweepEnRoute)
            {
                Collider[] cols = Physics.OverlapSphere(caster.transform.position, radius);
                foreach (Collider col in cols)
                {
                    Entity entity = col.GetComponentInParent<Entity>();
                    if (entity == null || entity == caster || entity.isDead) continue;
                    if (alreadyHit.Contains(entity)) continue;
                    if (!PassesAoeFilter(skill.aoeFaction, caster, entity)) continue;

                    alreadyHit.Add(entity);
                    ApplyEffectType(skill, caster, entity);
                    ApplyStatusEffects(skill, caster, entity);
                    CheckKill(entity);

                    if (skill.vfxImpact != null)
                        Instantiate(skill.vfxImpact, entity.transform.position, Quaternion.identity);
                    if (skill.soundEffect != null)
                        AudioSource.PlayClipAtPoint(skill.soundEffect, entity.transform.position);
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (caster != null)
        {
            if (caster is Mob endMob) endMob.IsDashing = false;
            if (agent != null) agent.enabled = true;

            if (!sweepEnRoute && !interrupted && !caster.isDead && target != null && !target.isDead)
            {
                ApplyEffectType(skill, caster, target);
                ApplyStatusEffects(skill, caster, target);
                CheckKill(target);

                if (skill.vfxImpact != null)
                    Instantiate(skill.vfxImpact, target.transform.position, Quaternion.identity);
                if (skill.soundEffect != null)
                    AudioSource.PlayClipAtPoint(skill.soundEffect, target.transform.position);
            }
        }
    }

    // =========================================================
    // FILTRE ALLIÉ/ENNEMI — effets de zone (SkillAoeFaction)
    // =========================================================
```

- [ ] **Step 9: Ajouter `TeleportSelfNow()` — juste après `DashSelfRoutine()`**

```csharp
    // =========================================================
    // TELEPORT SELF — le caster se téléporte instantanément (Target/GroundTarget/Cone)
    // =========================================================

    private void TeleportSelfNow(SkillData skill, Entity caster, Entity target)
    {
        Vector3 startPos = caster.transform.position;
        Vector3 destination;

        if (skill.targetType == TargetType.Target)
        {
            if (target == null || target.isDead) { LogMissingTarget(skill, caster); return; }
            // teleportBehindTarget calculé par rapport au FACING DE LA CIBLE, pas du caster —
            // "derrière" = du côté vers lequel elle tourne le dos (blink-backstab).
            Vector3 offsetDir = skill.teleportBehindTarget
                ? -target.transform.forward
                :  target.transform.forward;
            destination = target.transform.position + offsetDir.normalized * 1.5f;
        }
        else if (skill.targetType == TargetType.GroundTarget)
        {
            Vector3 point = _groundTargetPoint ?? startPos;
            _groundTargetPoint = null;
            Vector3 toPoint = point - startPos;
            float   dist    = toPoint.magnitude;
            destination = dist > skill.displacementDistance
                ? startPos + toPoint.normalized * skill.displacementDistance
                : point;
        }
        else // Cone
        {
            Vector3 dir = _skillDirection?.normalized ?? caster.transform.forward;
            _skillDirection = null;
            destination = startPos + dir * skill.displacementDistance;
        }

        if (!TryClampToNavMesh(destination, out destination)) return;

        WarpNow(caster, destination);

        // bringsAllies : téléporte aussi les alliés proches de la position ORIGINE, au même
        // décalage relatif vers la destination.
        if (skill.bringsAllies)
        {
            Vector3 delta = destination - startPos;
            Collider[] cols = Physics.OverlapSphere(startPos, skill.aoeRadius > 0f ? skill.aoeRadius : 5f);
            foreach (Collider col in cols)
            {
                Entity ally = col.GetComponentInParent<Entity>();
                if (ally == null || ally == caster || ally.isDead) continue;
                if (!IsAlly(caster, ally)) continue;

                Vector3 allyDest = ally.transform.position + delta;
                if (!TryClampToNavMesh(allyDest, out allyDest)) continue;
                WarpNow(ally, allyDest);
            }
        }

        // Dégâts/effets résolus À LA NOUVELLE POSITION — targetType réévalué après le saut (spec
        // §4 : un AoE_Self téléporté au milieu d'un groupe frappe ce qui l'entoure après le
        // saut, pas avant). Réutilise DispatchByTargetType tel quel : le déplacement a déjà eu
        // lieu, ce switch ne fait plus que le dégât/l'effet normal du skill.
        DispatchByTargetType(skill, caster, target);

        if (skill.vfxImpact != null)
            Instantiate(skill.vfxImpact, caster.transform.position, Quaternion.identity);
        if (skill.soundEffect != null)
            AudioSource.PlayClipAtPoint(skill.soundEffect, caster.transform.position);
    }

```

- [ ] **Step 10: Ajouter `PullRoutine()` — juste après `TeleportSelfNow()`**

```csharp
    // =========================================================
    // PULL — attire une/des cible(s) vers le caster ou un point (Target/GroundTarget/Cone/AoE_Self)
    // Dégâts/effets appliqués AU DÉPART (spec §4), avant le trajet animé.
    // =========================================================

    private IEnumerator PullRoutine(SkillData skill, Entity caster, Entity target)
    {
        List<Entity> victims = new List<Entity>();
        Vector3 anchor;
        bool    exactLanding; // GroundTarget : atterrit pile sur le point (étalé). Sinon :
                               // s'arrête à stopOffset de l'ancre (jamais dans le caster).

        if (skill.targetType == TargetType.Target)
        {
            if (target == null || target.isDead) { LogMissingTarget(skill, caster); yield break; }
            victims.Add(target);
            anchor = caster.transform.position;
            exactLanding = false;
        }
        else if (skill.targetType == TargetType.GroundTarget)
        {
            Vector3 point = _groundTargetPoint ?? caster.transform.position;
            _groundTargetPoint = null;
            anchor = point;
            victims = SelectZoneEntities(skill, caster, anchor, skill.aoeRadius);
            exactLanding = true;
        }
        else if (skill.targetType == TargetType.Cone)
        {
            Vector3 dir = _skillDirection?.normalized ?? caster.transform.forward;
            _skillDirection = null;
            anchor = caster.transform.position;
            victims = SelectConeEntities(skill, caster, dir);
            exactLanding = false;
        }
        else // AoE_Self
        {
            anchor = caster.transform.position;
            victims = SelectZoneEntities(skill, caster, anchor, skill.aoeRadius);
            exactLanding = false;
        }

        victims.RemoveAll(v => ResistsDisplacement(v));
        if (victims.Count == 0) yield break;

        foreach (Entity v in victims)
        {
            ApplyEffectType(skill, caster, v);
            ApplyStatusEffects(skill, caster, v);
            CheckKill(v);
            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, v.transform.position, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, v.transform.position);
        }

        float dashDuration = 0.25f;
        float stopOffset   = 1.2f;
        var   starts = new Vector3[victims.Count];
        var   ends   = new Vector3[victims.Count];
        var   agents = new UnityEngine.AI.NavMeshAgent[victims.Count];

        for (int i = 0; i < victims.Count; i++)
        {
            Entity v = victims[i];
            starts[i] = v.transform.position;

            Vector3 personalAnchor = anchor + SpreadOffset(i, victims.Count);
            Vector3 toAnchor = personalAnchor - starts[i];
            float   dist     = toAnchor.magnitude;
            Vector3 dir      = dist > 0.001f ? toAnchor.normalized : Vector3.zero;
            float   travel   = exactLanding ? dist : Mathf.Max(0f, dist - stopOffset);
            Vector3 dest     = starts[i] + dir * travel;
            TryClampToNavMesh(dest, out ends[i]);

            agents[i] = v.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agents[i] != null) agents[i].enabled = false;
            if (v is Mob vm) vm.IsDashing = true;
        }

        float elapsed = 0f;
        while (elapsed < dashDuration)
        {
            for (int i = 0; i < victims.Count; i++)
            {
                if (victims[i] == null || victims[i].isDead) continue;
                victims[i].transform.position = Vector3.Lerp(starts[i], ends[i], elapsed / dashDuration);
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        for (int i = 0; i < victims.Count; i++)
        {
            if (victims[i] == null) continue;
            if (!victims[i].isDead) victims[i].transform.position = ends[i];
            if (victims[i] is Mob endMob) endMob.IsDashing = false;
            if (agents[i] != null) agents[i].enabled = true;
        }
    }

```

- [ ] **Step 11: Ajouter `PushRoutine()` — juste après `PullRoutine()`**

```csharp
    // =========================================================
    // PUSH — repousse une/des cible(s) loin d'une origine (Target/GroundTarget/Cone/AoE_Self)
    // Dégâts/effets appliqués AU DÉPART (spec §4), avant le trajet animé.
    // =========================================================

    private IEnumerator PushRoutine(SkillData skill, Entity caster, Entity target)
    {
        List<Entity> victims = new List<Entity>();
        Vector3 origin;

        if (skill.targetType == TargetType.Target)
        {
            if (target == null || target.isDead) { LogMissingTarget(skill, caster); yield break; }
            victims.Add(target);
            origin = caster.transform.position;
        }
        else if (skill.targetType == TargetType.GroundTarget)
        {
            Vector3 point = _groundTargetPoint ?? caster.transform.position;
            _groundTargetPoint = null;
            origin = point;
            victims = SelectZoneEntities(skill, caster, origin, skill.aoeRadius);
        }
        else if (skill.targetType == TargetType.Cone)
        {
            Vector3 dir = _skillDirection?.normalized ?? caster.transform.forward;
            _skillDirection = null;
            origin = caster.transform.position;
            victims = SelectConeEntities(skill, caster, dir);
        }
        else // AoE_Self
        {
            origin = caster.transform.position;
            victims = SelectZoneEntities(skill, caster, origin, skill.aoeRadius);
        }

        victims.RemoveAll(v => ResistsDisplacement(v));
        if (victims.Count == 0) yield break;

        foreach (Entity v in victims)
        {
            ApplyEffectType(skill, caster, v);
            ApplyStatusEffects(skill, caster, v);
            CheckKill(v);
            if (skill.vfxImpact != null)
                Instantiate(skill.vfxImpact, v.transform.position, Quaternion.identity);
            if (skill.soundEffect != null)
                AudioSource.PlayClipAtPoint(skill.soundEffect, v.transform.position);
        }

        float dashDuration = 0.25f;
        var   starts = new Vector3[victims.Count];
        var   ends   = new Vector3[victims.Count];
        var   agents = new UnityEngine.AI.NavMeshAgent[victims.Count];

        for (int i = 0; i < victims.Count; i++)
        {
            Entity v = victims[i];
            starts[i] = v.transform.position;

            Vector3 dir = starts[i] - origin;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : caster.transform.forward;
            Vector3 dest = starts[i] + dir * skill.displacementDistance;
            TryClampToNavMesh(dest, out ends[i]);

            agents[i] = v.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agents[i] != null) agents[i].enabled = false;
            if (v is Mob vm) vm.IsDashing = true;
        }

        float elapsed = 0f;
        while (elapsed < dashDuration)
        {
            for (int i = 0; i < victims.Count; i++)
            {
                if (victims[i] == null || victims[i].isDead) continue;
                victims[i].transform.position = Vector3.Lerp(starts[i], ends[i], elapsed / dashDuration);
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        for (int i = 0; i < victims.Count; i++)
        {
            if (victims[i] == null) continue;
            if (!victims[i].isDead) victims[i].transform.position = ends[i];
            if (victims[i] is Mob endMob) endMob.IsDashing = false;
            if (agents[i] != null) agents[i].enabled = true;
        }
    }

```

- [ ] **Step 12: Ajouter `SwapPositionNow()` — juste après `PushRoutine()`**

```csharp
    // =========================================================
    // SWAP POSITION — caster et cible échangent leurs places, instantanément (Target uniquement)
    // Résisté = échange COMPLET annulé (spec §7) — jamais de swap "à moitié".
    // =========================================================

    private void SwapPositionNow(SkillData skill, Entity caster, Entity target)
    {
        if (target == null || target.isDead) { LogMissingTarget(skill, caster); return; }

        if (ResistsDisplacement(target)) return;

        Vector3 casterPos = caster.transform.position;
        Vector3 targetPos = target.transform.position;

        if (!TryClampToNavMesh(targetPos, out Vector3 casterDest)) return;
        if (!TryClampToNavMesh(casterPos, out Vector3 targetDest)) return;

        WarpNow(caster, casterDest);
        WarpNow(target, targetDest);

        DispatchByTargetType(skill, caster, target);

        if (skill.vfxImpact != null)
            Instantiate(skill.vfxImpact, targetDest, Quaternion.identity);
        if (skill.soundEffect != null)
            AudioSource.PlayClipAtPoint(skill.soundEffect, casterDest);
    }

```

- [ ] **Step 13: Compiler — DOIT être propre (0 erreur) une fois combiné avec la Tâche 4**

Ce fichier seul ne compile PAS encore (Tâche 4 restante — `SkillBar.cs`/`CombatAIController.cs`
référencent encore `TargetType.Dash_Target`/`.Dash_Direction`). Vérifier juste qu'aucune ERREUR
n'apparaît en dehors de ces 2 fichiers.

- [ ] **Step 14: Commit**

```bash
git add Combat/SkillSystem.cs
git commit -m "feat: SkillSystem — StartDisplacement (DashSelf/TeleportSelf/Pull/Push/SwapPosition)

New top-level entry point bypassing DispatchByTargetType, same pattern
as StartTrajectory/PlantDelayedZone. Replaces DashToTarget/
DashInDirection (generalized to Target/GroundTarget/Cone) and the
9-case SkillSpecialEffect displacement switch (replaced by Pull/Push/
SwapPosition, animated + aoeFaction-filtered unlike the deleted
instant/unfiltered DisplacementUtils calls). Shared helpers:
ResistsDisplacement (equipment/Mob/PNJ resistance roll),
TryClampToNavMesh (extracted from DisplacementUtils.WarpToNavMesh for
animated destinations), SelectZoneEntities/SelectConeEntities (entity
selection, reuses ExecuteCone's angle filter), SpreadOffset
(anti-stack for zone Pull), IsHardCCd (DashSelf interruption only,
Pull/Push targets are never CC-immune to being displaced).

Still doesn't compile alone - SkillBar.cs/CombatAIController.cs fixed
next."
```

---

### Task 4: `Data/Skills/SkillBar.cs` + `Combat/CombatAIController.cs` — branchement dispatch

**Files:**
- Modify: `Data/Skills/SkillBar.cs`
- Modify: `Combat/CombatAIController.cs`

**Interfaces:**
- Consumes: `SkillSystem.StartDisplacement()` (Tâche 3), `SkillData.displacementType` (Tâche 1).
- Produces: rien de nouveau consommé par une tâche suivante — dernier fichier de code du
  chantier, **doit compiler proprement après cette tâche**.

Ces 4 sites suivent tous le MÊME changement mécanique — un nouveau palier de priorité AVANT
`isTrajectory`. Bon candidat pour un dispatch batché (les 4 dans le même changement), pas 4 tâches
séparées.

- [ ] **Step 1: `SkillBar.ResolveInstant()` — ajouter le palier `displacementType`**

Remplacer :

```csharp
        // isTrajectory vérifié EN PREMIER — un skill Cone/Target/GroundTarget avec les deux flags
        // cochés (combo autorisé, voir SkillData.isTrajectory) doit passer par StartTrajectory(),
        // qui plante lui-même la zone différée en plus du dégât immédiat. Priorité inversée sans
        // risque pour tout le reste : un skill qui n'a qu'un seul des deux flags actif se
        // comporte identiquement peu importe l'ordre des checks.
        if (skill.isTrajectory)
            SkillSystem.Instance?.StartTrajectory(skill, _player, target);
        else if (skill.hasDelayedImpact)
            SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
        else
            SkillSystem.Instance?.ResolveExecute(skill, _player, target);

        _cooldownTimers[slot] = skill.cooldown;
        if (slot >= 1)
        {
            _gcdTimer = GCD_DURATION;
            float autoAttackDelay = skill.attackAnimation != null
                ? Mathf.Max(GCD_DURATION, skill.attackAnimation.length)
                : GCD_DURATION;
            TargetingSystem.Instance?.DelayAutoAttack(autoAttackDelay);
        }
    }

    // ── MultiHit (chantier B) ──────────────────────────────────
```

par :

```csharp
        // displacementType vérifié EN PREMIER — mutuellement exclusif avec isTrajectory/
        // hasDelayedImpact (voir SkillData.OnValidate), jamais les deux en même temps sur un
        // skill correctement configuré, mais l'ordre compte pour un skill mal configuré : le
        // déplacement prend le dessus plutôt que d'être silencieusement ignoré.
        if (skill.displacementType != DisplacementType.None)
            SkillSystem.Instance?.StartDisplacement(skill, _player, target);
        // isTrajectory vérifié ENSUITE — un skill Cone/Target/GroundTarget avec les deux flags
        // cochés (combo autorisé, voir SkillData.isTrajectory) doit passer par StartTrajectory(),
        // qui plante lui-même la zone différée en plus du dégât immédiat. Priorité inversée sans
        // risque pour tout le reste : un skill qui n'a qu'un seul des deux flags actif se
        // comporte identiquement peu importe l'ordre des checks.
        else if (skill.isTrajectory)
            SkillSystem.Instance?.StartTrajectory(skill, _player, target);
        else if (skill.hasDelayedImpact)
            SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
        else
            SkillSystem.Instance?.ResolveExecute(skill, _player, target);

        _cooldownTimers[slot] = skill.cooldown;
        if (slot >= 1)
        {
            _gcdTimer = GCD_DURATION;
            float autoAttackDelay = skill.attackAnimation != null
                ? Mathf.Max(GCD_DURATION, skill.attackAnimation.length)
                : GCD_DURATION;
            TargetingSystem.Instance?.DelayAutoAttack(autoAttackDelay);
        }
    }

    // ── MultiHit (chantier B) ──────────────────────────────────
```

- [ ] **Step 2: `SkillBar.ResolveChannel()` — ajouter le palier `displacementType`**

Remplacer :

```csharp
        // Ordre inversé — voir commentaire équivalent dans SkillBar.ResolveInstant().
        if (skill.isTrajectory)
            SkillSystem.Instance?.StartTrajectory(skill, _player, target);
        else if (skill.hasDelayedImpact)
            SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
        else
            SkillSystem.Instance?.Execute(skill, _player, target);

        _cooldownTimers[slot] = skill.cooldown;
        if (slot >= 1)
        {
            _gcdTimer = GCD_DURATION;
            TargetingSystem.Instance?.DelayAutoAttack(GCD_DURATION);
        }
    }
```

par :

```csharp
        // Même ordre de priorité que SkillBar.ResolveInstant().
        if (skill.displacementType != DisplacementType.None)
            SkillSystem.Instance?.StartDisplacement(skill, _player, target);
        else if (skill.isTrajectory)
            SkillSystem.Instance?.StartTrajectory(skill, _player, target);
        else if (skill.hasDelayedImpact)
            SkillSystem.Instance?.PlantDelayedZone(skill, _player, target);
        else
            SkillSystem.Instance?.Execute(skill, _player, target);

        _cooldownTimers[slot] = skill.cooldown;
        if (slot >= 1)
        {
            _gcdTimer = GCD_DURATION;
            TargetingSystem.Instance?.DelayAutoAttack(GCD_DURATION);
        }
    }
```

- [ ] **Step 3: `CombatAIController.ResolvePendingHit()` — ajouter le palier `displacementType`**

Remplacer :

```csharp
        else
        {
            // isTrajectory vérifié EN PREMIER — un skill Cone/Target/GroundTarget avec les deux flags
            // cochés (combo autorisé, voir SkillData.isTrajectory) doit passer par
            // StartTrajectory(), qui plante lui-même la zone différée en plus du dégât immédiat.
            // Priorité inversée sans risque pour tout le reste : un skill qui n'a qu'un seul des
            // deux flags actif se comporte identiquement peu importe l'ordre des checks.
            if (skill.isTrajectory)
                _skillSystem?.StartTrajectory(skill, _owner, target);
            else if (skill.hasDelayedImpact)
                _skillSystem?.PlantDelayedZone(skill, _owner, target);
            else
                _skillSystem?.ResolveExecute(skill, _owner, target);
        }
```

par :

```csharp
        else
        {
            // displacementType vérifié EN PREMIER — voir commentaire équivalent dans
            // SkillBar.ResolveInstant().
            if (skill.displacementType != DisplacementType.None)
                _skillSystem?.StartDisplacement(skill, _owner, target);
            // isTrajectory vérifié ENSUITE — un skill Cone/Target/GroundTarget avec les deux flags
            // cochés (combo autorisé, voir SkillData.isTrajectory) doit passer par
            // StartTrajectory(), qui plante lui-même la zone différée en plus du dégât immédiat.
            // Priorité inversée sans risque pour tout le reste : un skill qui n'a qu'un seul des
            // deux flags actif se comporte identiquement peu importe l'ordre des checks.
            else if (skill.isTrajectory)
                _skillSystem?.StartTrajectory(skill, _owner, target);
            else if (skill.hasDelayedImpact)
                _skillSystem?.PlantDelayedZone(skill, _owner, target);
            else
                _skillSystem?.ResolveExecute(skill, _owner, target);
        }
```

- [ ] **Step 4: `CombatAIController.ResolveChannelCast()` — ajouter le palier `displacementType`**

Remplacer :

```csharp
        // Ordre inversé — voir commentaire équivalent dans ResolvePendingHit().
        if (skill.isTrajectory)
            _skillSystem?.StartTrajectory(skill, _owner, target);
        else if (skill.hasDelayedImpact)
            _skillSystem?.PlantDelayedZone(skill, _owner, target);
        else
            _skillSystem?.Execute(skill, _owner, target);

        if (_profile.SecondarySkills != null && _profile.SecondarySkills.Contains(skill))
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
    }
```

par :

```csharp
        // Même ordre de priorité — voir commentaire équivalent dans ResolvePendingHit().
        if (skill.displacementType != DisplacementType.None)
            _skillSystem?.StartDisplacement(skill, _owner, target);
        else if (skill.isTrajectory)
            _skillSystem?.StartTrajectory(skill, _owner, target);
        else if (skill.hasDelayedImpact)
            _skillSystem?.PlantDelayedZone(skill, _owner, target);
        else
            _skillSystem?.Execute(skill, _owner, target);

        if (_profile.SecondarySkills != null && _profile.SecondarySkills.Contains(skill))
            _skillCooldowns[skill] = skill.cooldown > 0f ? skill.cooldown : 6f;
    }
```

- [ ] **Step 5: Compiler — DOIT être propre (0 erreur) à ce stade**

C'est le premier point du plan où le projet doit compiler sans erreur (Tâches 1-4 combinées).
Ouvrir Unity (ou `Console` si déjà ouvert) et vérifier 0 erreur de compilation.

- [ ] **Step 6: Grep de non-régression**

Depuis `Assets/Scripts` :

```bash
grep -rn "TargetType\.\(Dash_Target\|Dash_Direction\)" --include=*.cs .
grep -rn "SkillSpecialEffect\.\(Pull\|Push\|SwapPosition\|PullAoE\|PushAoE\|GatherAoE\|Vortex\|TeleportSelf\|TeleportTarget\)" --include=*.cs .
grep -rn "pullPushForce" --include=*.cs .
```

Expected pour les 3 commandes : **aucune sortie**. Si quelque chose remonte, corriger avant de
continuer.

- [ ] **Step 7: Commit**

```bash
git add Data/Skills/SkillBar.cs Combat/CombatAIController.cs
git commit -m "feat: wire displacementType to the existing priority chain (Player + Mob/PNJ)

SkillBar.ResolveInstant/ResolveChannel AND CombatAIController.
ResolvePendingHit/ResolveChannelCast all gain the same new first
priority tier: displacementType != None -> StartDisplacement(),
checked before isTrajectory/hasDelayedImpact. Full Mob/PNJ parity,
not just the player path - CombatAIController already dispatches the
same skills through the same chain.

This is the first commit in this plan where the project compiles
clean."
```

---

### Task 5: `Data/Mobs/MobData.cs` + `Data/PNJ/PNJData.cs` + `Entities/Mob.cs` + `Entities/PNJ.cs` — résistance innée

**Files:**
- Modify: `Data/Mobs/MobData.cs`
- Modify: `Data/PNJ/PNJData.cs`
- Modify: `Entities/Mob.cs`
- Modify: `Entities/PNJ.cs`

**Interfaces:**
- Consumes: `DebuffType.Displacement` (Tâche 2, mais ce champ fonctionne pour N'IMPORTE QUEL
  `DebuffType`, pas seulement Displacement — voir spec §7).
- Produces: rien consommé par une tâche suivante.

Indépendant des Tâches 1/3/4 (aucune dépendance de compilation croisée) — peut être fait à
n'importe quel moment après la Tâche 2, mais garder son propre scope de review (fichiers
différents, zéro risque de conflit).

- [ ] **Step 1: `MobData.cs` — ajouter le champ `debuffResistances`**

Trouver :

```csharp
    // ── Effets On-Hit ─────────────────────────────────────────
    [Header("Effets On-Hit reçus")]
    public List<OnHitReceivedEffectEntry> onHitReceivedEffects = new List<OnHitReceivedEffectEntry>();
    [Header("Effets On-Hit infligés")]
    public List<OnHitDealtEffectEntry> onHitDealtEffects = new List<OnHitDealtEffectEntry>();
```

Remplacer par :

```csharp
    // ── Effets On-Hit ─────────────────────────────────────────
    [Header("Effets On-Hit reçus")]
    public List<OnHitReceivedEffectEntry> onHitReceivedEffects = new List<OnHitReceivedEffectEntry>();
    [Header("Effets On-Hit infligés")]
    public List<OnHitDealtEffectEntry> onHitDealtEffects = new List<OnHitDealtEffectEntry>();

    // ── Résistances aux debuffs ────────────────────────────────
    [Header("Résistances aux debuffs (innées, indépendantes de tout équipement)")]
    [Tooltip("Même mécanisme que la résistance équipement du joueur (DebuffResistanceEntry) — " +
             "un Mob n'a pas d'équipement, ce champ le remplace. resistChance = 1 sur un " +
             "DebuffType = immunité totale (ex: boss raciné, immunisé au CC dur — voir " +
             "DebuffType.Displacement pour l'immunité au Pull/Push/SwapPosition).")]
    public List<DebuffResistanceEntry> debuffResistances = new List<DebuffResistanceEntry>();
```

- [ ] **Step 2: `PNJData.cs` — ajouter le champ `debuffResistances`**

Trouver :

```csharp
    // ── Effets On-Hit — actifs même hors canFight (un garde peut avoir Thorns) ──
    [Header("Effets On-Hit reçus")]
    public List<OnHitReceivedEffectEntry> onHitReceivedEffects = new List<OnHitReceivedEffectEntry>();
    [Header("Effets On-Hit infligés")]
    [ShowIf(nameof(canFight), true)]
    public List<OnHitDealtEffectEntry> onHitDealtEffects = new List<OnHitDealtEffectEntry>();
```

Remplacer par :

```csharp
    // ── Effets On-Hit — actifs même hors canFight (un garde peut avoir Thorns) ──
    [Header("Effets On-Hit reçus")]
    public List<OnHitReceivedEffectEntry> onHitReceivedEffects = new List<OnHitReceivedEffectEntry>();
    [Header("Effets On-Hit infligés")]
    [ShowIf(nameof(canFight), true)]
    public List<OnHitDealtEffectEntry> onHitDealtEffects = new List<OnHitDealtEffectEntry>();

    // ── Résistances aux debuffs — actives même hors canFight, même raison que
    // onHitReceivedEffects ci-dessus (un PNJ non-combattant peut quand même être attaqué) ──
    [Header("Résistances aux debuffs (innées, indépendantes de tout équipement)")]
    [Tooltip("Même mécanisme que la résistance équipement du joueur (DebuffResistanceEntry) — " +
             "un PNJ n'a pas d'équipement, ce champ le remplace. resistChance = 1 sur un " +
             "DebuffType = immunité totale.")]
    public List<DebuffResistanceEntry> debuffResistances = new List<DebuffResistanceEntry>();
```

- [ ] **Step 3: `Mob.cs` — câbler `debuffResistances` dans `ApplyData()`**

Trouver (fin de `ApplyData()`, juste avant `SnapshotBaseStats();`) :

```csharp
        // ── Résistances élémentaires — profil fixe du SO ─────
        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
            SetElementalResistance(e, data.GetElementalResistance(e));
 
        // ── Snapshot — base pour buffs/debuffs ────────────────
        SnapshotBaseStats();
```

Remplacer par :

```csharp
        // ── Résistances élémentaires — profil fixe du SO ─────
        foreach (ElementType e in System.Enum.GetValues(typeof(ElementType)))
            SetElementalResistance(e, data.GetElementalResistance(e));

        // ── Résistances aux debuffs — innées, indépendantes de l'équipement (les Mobs n'en ont
        // pas). CharacterStats.ApplyDebuffResistances() est strictement réservée au Player, donc
        // sans ce câblage statusEffects._debuffResistances resterait TOUJOURS vide pour un Mob.
        if (data.debuffResistances != null)
            foreach (var entry in data.debuffResistances)
                statusEffects.SetDebuffResistance(entry.debuffType, entry.resistChance);

        // ── Snapshot — base pour buffs/debuffs ────────────────
        SnapshotBaseStats();
```

- [ ] **Step 4: `PNJ.cs` — câbler `debuffResistances` dans `Awake()`, APRÈS `base.Awake()`**

⚠ Important : `statusEffects` (propriété sur `Entity`) n'est initialisée que par
`Entity.Awake()` — `base.Awake()` doit avoir déjà tourné avant de lire `statusEffects`. Le bloc
`if (data != null) { ... }` de `PNJ.Awake()` tourne AVANT `base.Awake()` (voir code ci-dessous),
donc ce câblage va APRÈS, pas dans ce premier bloc.

Trouver :

```csharp
        base.Awake();

        // Fige le snapshot — RequestRecalculate() repartira de ces valeurs
        SnapshotBaseStats();
```

Remplacer par :

```csharp
        base.Awake();

        // Résistances aux debuffs — innées, indépendantes de l'équipement (les PNJ n'en ont
        // pas). Même raison que Mob.ApplyData() : CharacterStats.ApplyDebuffResistances() est
        // strictement réservée au Player. APRÈS base.Awake() — statusEffects n'est initialisée
        // que là (Entity.Awake()).
        if (data != null && data.debuffResistances != null)
            foreach (var entry in data.debuffResistances)
                statusEffects.SetDebuffResistance(entry.debuffType, entry.resistChance);

        // Fige le snapshot — RequestRecalculate() repartira de ces valeurs
        SnapshotBaseStats();
```

- [ ] **Step 5: Compiler**

Doit compiler proprement — ces 4 fichiers n'ont aucune dépendance croisée avec les Tâches 1-4 au
-delà de `DebuffType`/`DebuffResistanceEntry` (déjà existants/Tâche 2).

- [ ] **Step 6: Commit**

```bash
git add Data/Mobs/MobData.cs Data/PNJ/PNJData.cs Entities/Mob.cs Entities/PNJ.cs
git commit -m "feat: innate Mob/PNJ debuff resistance (fixes Player-only CharacterStats gap)

MobData/PNJData gain debuffResistances (List<DebuffResistanceEntry>,
same type equipment already uses), wired the same place onHitReceived
Effects/onHitDealtEffects already live. CharacterStats.
ApplyDebuffResistances() is Player-only (reads equipment instances) -
without this, a Mob/PNJ could never resist ANY debuff, not just
Displacement, and specifically could never resist a player's Pull/
Push. resistChance: 1.0 on any DebuffType is now available on MobData
for guaranteed immunity (e.g. a rooted boss)."
```

---

### Task 6: Migration de `skl_loup_special_test.asset`

**Files:**
- Modify: `../Content/Entity/Mobs/Wolf_test/skl_loup_special_test.asset`

**Interfaces:**
- Consumes: la nouvelle numérotation `TargetType`/`DisplacementType` (Tâche 1).
- Produces: rien (dernière dépendance de contenu du chantier).

⚠ Ce fichier vit sous `Assets/Content`, HORS du repo git (`Assets/Scripts`) — **aucun `.git` dans
`Assets/Content` ni à la racine `C:\AetherTree_v3.5`**, vérifié lors du chantier TargetType
précédent (l'implémenteur de l'époque a été bloqué en essayant de `git add`/`git commit` ce
fichier avant que le contrôleur ne le débloque). L'édition se fait quand même — fichier texte
Unity YAML simple, pas besoin d'ouvrir Unity — mais **AUCUN commit n'est possible pour ce
fichier**. Ne pas essayer de `git add`/`git commit` dessus.

- [ ] **Step 1: Éditer le fichier**

Chemin (relatif à `Assets/Scripts`) : `../Content/Entity/Mobs/Wolf_test/
skl_loup_special_test.asset`.

Ce skill utilisait `targetType: 6` (ancien ordinal `Dash_Target`). Le nouvel ordinal `Target` est
`0` — DÉJÀ la valeur qu'avait `Target` dans l'ANCIEN enum aussi (`Target = 0` inchangé entre
ancien et nouvel enum), donc chercher la ligne `targetType: 6` et la remplacer par
`targetType: 0`.

Ajouter aussi une nouvelle ligne `displacementType: 1` (DashSelf) — chercher un champ YAML
existant proche (ex: `targetType:`) pour insérer la nouvelle ligne juste après, au même niveau
d'indentation. Le format Unity `.asset` sérialise les champs manquants avec leur valeur par
défaut au chargement (déjà vérifié sans risque lors du chantier précédent) — si `displacementType`
n'existe pas encore dans ce fichier au moment de l'édition, ajouter la ligne explicitement plutôt
que de compter sur le défaut, pour que ce skill reste un dash fonctionnel dès le chargement.

- [ ] **Step 2: Vérifier qu'aucun autre `.asset` n'a été oublié**

```bash
grep -rl "targetType: 6$\|targetType: 7$" --include=*.asset ../Content 2>/dev/null
```

Expected : **aucune sortie** (le seul fichier à ces ordinaux vient d'être migré).

```bash
grep -rl "specialEffect: [1-9]$" --include=*.asset ../Content 2>/dev/null
```

Expected : **aucune sortie** (déjà vérifié en Tâche 1 Step 1, re-vérifié ici par sécurité).

- [ ] **Step 3: Pas de commit possible**

Documenter dans le rapport de tâche : fichier édité et vérifié, mais AUCUN commit git — ce
fichier n'est sous aucun contrôle de version accessible à ce plan.

---

### Task 7: `docs/guide-utilisation/creation-skills.md` — mise à jour du guide

**Files:**
- Modify: `docs/guide-utilisation/creation-skills.md`

**Interfaces:**
- Consumes: la liste `DisplacementType`/matrice verbe×targetType (Tâche 1) et
  `DebuffType.Displacement`/`debuffResistances` (Tâches 2/5).
- Produces: rien (documentation seule, aucune dépendance de code).

Peut être fait en parallèle des Tâches 2-6 une fois la Tâche 1 connue (aucune dépendance de
compilation — fichier Markdown).

- [ ] **Step 1: Mettre à jour la ligne `specialEffect` dans le tableau ③**

Trouver :

```markdown
| `specialEffect` | Actif seulement si `effectType = Other` — Pull/Push/SwapPosition/PullAoE/PushAoE/GatherAoE/Vortex/TeleportSelf/TeleportTarget/DrainHP/DrainMana/Summon/Interrupt. Champs additionnels (`pullPushForce`, `drainHealRatio`, `summonMobData`…) apparaissent selon la valeur choisie. |
```

Remplacer par :

```markdown
| `specialEffect` | Actif seulement si `effectType = Other` — DrainHP/DrainMana/Summon/Interrupt uniquement (le déplacement — Pull/Push/SwapPosition/Téléportation/Dash — vit maintenant dans `displacementType`, ⑥bis ci-dessous, composable avec N'IMPORTE QUEL `effectType`). Champs additionnels (`drainHealRatio`, `summonMobData`…) apparaissent selon la valeur choisie. |
```

- [ ] **Step 2: Remplacer le tableau `targetType` (8 lignes → 6) dans la section ⑥**

Trouver le tableau actuel (8 lignes, `Target` à `Dash_Direction`) et son en-tête :

```markdown
**`targetType` — les 8 possibilités, aucune n'est redondante malgré des géométries qui se
ressemblent :**

| `targetType` | Cible verrouillée ? | Portée / zone | Comportement | Cas d'usage typique |
|---|---|---|---|---|
| `Target` | **Oui** | `range` = distance max au clic | Touche la cible verrouillée. + `isTrajectory` : perce tout jusqu'à elle (position figée au lancement, pas de homing) — décoche `stopAtFirstHit` pour percer, coche-le pour s'arrêter au 1er ennemi touché (ex: lance qui transperce vs flèche qui s'arrête). | Sort mono-cible, ou lance qui transperce jusqu'à une cible verrouillée. |
| `Self` | Non | — | Touche uniquement le caster. | Buff/heal/shield sur soi. |
| `AoE_Self` | Non | `aoeRadius`, centré sur le **caster** | Sphère autour de toi, touche tout ce qui passe `aoeFaction`. | Explosion/aura centrée sur soi. |
| `AoE_Target` | **Oui** | `aoeRadius`, centré sur la **cible** | Sphère autour de la cible verrouillée (pas autour de toi). | Impact qui éclabousse autour d'un ennemi. |
| `GroundTarget` | Non (point au sol, visé à la souris) | `aoeRadius`, centré sur le point cliqué | Sphère à un point choisi au sol. + `isTrajectory` : voyage vers ce point (mur/vague qui avance vers où tu cliques) — `stopAtFirstHit` disponible pareil que `Target`. | Météore, zone posée au sol, ou mur de feu qui voyage vers la souris. |
| `Cone` | Non, **visé à la souris** | `range` (portée), `coneHalfAngle` (demi-angle en degrés) | Éventail devant le caster, orienté vers la souris (priorité à la cible engagée/sélectionnée si il y en a une) — touche tout ce qui est dans l'angle ET la portée. | Souffle, attaque en arc dirigée par le joueur. |
| `Dash_Target` | **Oui** | — (offset d'arrêt fixe côté code) | Le **CASTER** se déplace jusqu'à la cible (s'arrête juste avant). Ne fait AUCUN dégât par lui-même — c'est un déplacement pur. | Charge/gap-closer vers une cible. |
| `Dash_Direction` | Non | `range` (distance du dash, défaut 6) | Le **CASTER** se déplace en ligne droite dans sa direction de face. Ne fait aucun dégât par lui-même. | Esquive/dash directionnel. |
```

Remplacer par :

```markdown
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
```

- [ ] **Step 3: Ajouter une nouvelle section ⑥bis, juste après la section ⑥ existante (avant
      `### ⑦ Zone à impact différé...`)**

Trouver le point de jonction exact — la dernière ligne de la section ⑥ actuelle :

```markdown
`Target`/`GroundTarget`/`AoE_Target`/`Cone` peuvent en plus
devenir des trajectoires mobiles (hitbox qui voyage/s'élargit) via `isTrajectory` — voir ⑦.

### ⑦ Zone à impact différé / Trajectoire mobile
```

Remplacer par :

```markdown
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
`Cone`, `Push` toujours) · `teleportBehindTarget` (`TeleportSelf` + `Target` uniquement, coché =
derrière) · `bringsAllies` (`TeleportSelf` uniquement, emmène les alliés proches).

**Interruption & résistance** : un CC dur (Stun/Shocked/Freeze/Knockback/Fear) qui atterrit
PENDANT un `DashSelf` en cours l'interrompt à la position courante — `TeleportSelf`/
`SwapPosition` sont instantanés, pas de fenêtre à interrompre. L'ÉTAT de CC actuel de la cible
d'un `Pull`/`Push`/`SwapPosition` ne bloque JAMAIS le déplacement (une cible stun ne peut pas
résister) — mais sa RÉSISTANCE ÉQUIPEMENT/INNÉE (`DebuffType.Displacement`, même mécanisme que
les autres debuffs, roulé indépendamment par cible en zone) le peut. Un `MobData`/`PNJData` avec
`debuffResistances = [{ Displacement, 1.0 }]` est immunisé (ex: boss raciné) — voir
`onHitReceivedEffects`/`onHitDealtEffects` pour le même genre de champ sur ces deux SO.

### ⑦ Zone à impact différé / Trajectoire mobile
```

- [ ] **Step 4: Remplacer l'exemple "Effet spécial" (`skl_grappin`) qui utilisait l'ancien
      `specialEffect = Pull`**

Trouver :

```markdown
### Effet spécial (`effectType = Other`)

Ni dégâts classiques ni statusEffect pur — `specialEffect` pilote un comportement dédié.
Exemple : `skl_grappin` (tire la cible vers le caster).

| Champ | Valeur |
|---|---|
| `effectType` | Other |
| `specialEffect` | Pull |
| `pullPushForce` | 6 |
| `targetType` | Target |
| `range` | 8 |
| `damageMultiplier` | 0 (pas de dégâts, juste le déplacement) |
| `cooldown` | 12 |

Autres `specialEffect` disponibles suivant le même principe (un seul champ dédié apparaît selon
le choix) : `Push`/`SwapPosition`/`PullAoE`/`PushAoE`/`GatherAoE`/`Vortex` (déplacement),
`TeleportSelf`/`TeleportTarget`, `DrainHP` (+ `drainHealRatio`, ex: 0.5 = 50% des dégâts infligés
rendus en soin) / `DrainMana`, `Summon` (+ `summonMobData`/`summonDuration`), `Interrupt`.
```

Remplacer par :

```markdown
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
```

- [ ] **Step 5: Commit**

```bash
git add docs/guide-utilisation/creation-skills.md
git commit -m "docs: update creation-skills.md for displacementType (Dash/Teleport/Pull/Push/Swap)"
```

---

### Task 8: Vérification finale — grep global + Play Mode manuel (Florian)

**Files:** aucun (vérification pure, pas de modification).

**Interfaces:**
- Consumes: l'intégralité des Tâches 1-7.

- [ ] **Step 1: Grep final sur tout le repo**

```bash
cd Assets/Scripts
grep -rn "TargetType\.\(Dash_Target\|Dash_Direction\)" --include=*.cs .
grep -rn "SkillSpecialEffect\.\(Pull\|Push\|SwapPosition\|PullAoE\|PushAoE\|GatherAoE\|Vortex\|TeleportSelf\|TeleportTarget\)" --include=*.cs .
grep -rn "pullPushForce" --include=*.cs .
grep -rln "targetType: 6$\|targetType: 7$" --include=*.asset ../Content
grep -rln "specialEffect: [1-9]$" --include=*.asset ../Content
```

Expected pour les 5 commandes : **aucune sortie**.

- [ ] **Step 2: Vérifier `DisplacementUtils.cs`**

`WarpEntity`/`ApplyDisplacementAoE`/`GatherAoE` doivent être du code mort (plus aucun appelant
après la Tâche 3, Step 4). Vérifier :

```bash
grep -rn "DisplacementUtils\.\(WarpEntity\|ApplyDisplacementAoE\|GatherAoE\)" --include=*.cs .
```

Expected : **aucune sortie**. `DisplacementUtils.WarpToNavMesh` DOIT en revanche avoir des
appelants (Tâche 3 — `TeleportSelfNow`/`SwapPositionNow` via `WarpNow`, qui appelle directement
`agent.Warp`/`transform.position`, PAS `WarpToNavMesh` lui-même — voir note ci-dessous).

⚠ Note de cohérence trouvée en rédigeant ce plan : `WarpNow()` (Tâche 3, Step 6) réimplémente
directement le warp (agent ou transform) plutôt que d'appeler `DisplacementUtils.WarpToNavMesh()`
— parce que la destination est DÉJÀ clampée par `TryClampToNavMesh()` avant l'appel, et
`WarpToNavMesh()` re-clamperait une deuxième fois (inoffensif mais redondant). Si ce grep ne
trouve aucun appelant de `WarpToNavMesh` du tout, c'est ATTENDU — la méthode devient orpheline
mais reste dans `DisplacementUtils.cs` (pas supprimée, voir Global Constraints) au cas où un futur
skill l'utiliserait directement sans passer par le pipeline animé de `SkillSystem`. Ne pas
traiter ça comme un bug à corriger.

- [ ] **Step 3: Compilation Unity propre**

Ouvrir Unity, attendre la recompilation, vérifier 0 erreur ET 0 nouveau warning inattendu dans la
Console au chargement de la scène.

- [ ] **Step 4-15 : Checklist Play Mode (geste Florian, pas automatisable)**

Reprendre l'intégralité de la section "Vérification" de la spec
(`docs/superpowers/specs/2026-09-22-skill-displacement-design.md`, 12 points) :

1. Les 5 verbes × leurs colonnes supportées (15 combinaisons réellement utiles, voir spec §3).
2. `skl_loup_special_test.asset` (le loup migré) — toujours fonctionnel en jeu, dash bien vers sa
   cible.
3. Un CC dur qui atterrit PENDANT un `DashSelf` l'interrompt à la position courante ; un
   Pull/Push sur une cible déjà CC ne bloque PAS le déplacement.
4. NavMesh clamp — tester un `DashSelf`/`TeleportSelf` vers un point proche d'un mur/bord, vérifie
   qu'il n'y a pas de téléportation hors-mesh.
5. `bringsAllies` (`TeleportSelf`) — embarque bien les alliés proches, bon décalage relatif.
6. Étalement visuel d'un `Pull` en zone sur plusieurs cibles (pas de stack exact).
7. Résistance équipement/`DebuffResistanceEntry` sur `Displacement` — résiste bien un Pull ET un
   Push, jet indépendant par cible en zone.
8. Côté Mob/PNJ — un Mob avec un skill `displacementType` configuré l'exécute correctement via
   `CombatAIController`.
9. Warning Console si `displacementType` + `isTrajectory`/`hasDelayedImpact` cochés ensemble sur
   un skill de test.
10. Résistance innée Mob/PNJ — `debuffResistances = [{ Displacement, 1.0 }]` sur un `MobData` de
    test bloque bien tout Pull/Push venant du joueur ; un Mob SANS entrée se fait Pull/Push
    normalement (pas de régression).
11. `SwapPosition` sur un skill `targetType=Target` de test — caster et cible échangent bien
    leurs places, les deux nouvelles positions sont valides.
12. Rejouer les points 1-10 de la checklist Play Mode du chantier TargetType précédent
    (stopAtFirstHit, Cone visé souris, GroundTarget+Box, etc.) — confirmer qu'aucune régression
    n'a été introduite par ce chantier sur les mécaniques déjà existantes (`DispatchByTargetType`
    a perdu 2 `case`, `ApplySpecialEffect` a perdu 9 `case` — les autres chemins ne devraient pas
    avoir bougé, mais un geste de non-régression reste la seule vraie garantie).

Ces points nécessitent du geste manuel en Play Mode (viser à la souris, observer visuellement un
trajet/une résistance/un swap) — pas automatisable par un agent, à réaliser par Florian.
