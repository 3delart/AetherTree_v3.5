using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;

// =============================================================
// SKILLDATA — ScriptableObject définissant un sort
// Path : Assets/Scripts/Data/Skills/SkillData.cs
// AetherTree GDD v3.5 — Section 4
//
// Ordre Inspector :
//   ① Identité           — nom, description, tags, skillType
//   ② Compatibilité arme
//   ③ Effet principal    — effectType, cooldown, castTime, effet spécial (Other), dégâts
//                           physiques, éléments & multiplicateur élémentaire
//   ④ Exécution avancée  — Normal / MultiHit (hitSteps) / ComboSequence (comboSteps)
//   ⑤ Coût               — mana, HP, gold
//   ⑥ Ciblage & Portée   — targetType, range, aoeRadius, coneHalfAngle, aoeFaction
//   ⑦ Zone à impact différé / Trajectoire mobile — hasDelayedImpact, isTrajectory
//   ⑧ Effets secondaires (StatusEffects SO)
//   ⑨ Visuel             — icon, vfx*, animations
//   ⑩ Son                — soundEffect
//
// Calcul des dégâts (GDD §6.2) :
//   Physique  — baseDamage * damageMultiplier * ratio → réduit par défense
//     mRed = (dmg*meleeRatio)²  / (dmg*meleeRatio  + def * 1.5)
//     rRed = (dmg*rangedRatio)² / (dmg*rangedRatio + def * 1.5)
//     gRed = (dmg*magicRatio)²  / (dmg*magicRatio  + def * 1.5)
//     physDamage = mRed + rRed + gRed
//
//   Élémentaire — indépendant de l'arme, basé sur les elemPoints du joueur
//     elemDamage = elemPoints * elementalMultiplier
//     elemDamage *= (1 - résistance effective)
//
//   Total = physDamage + elemDamage
//
// Ratios de dégâts (damageMeleeRatio + damageRangedRatio + damageMagicRatio = 1.0) :
//   Ex: skill mêlée pur      → Melee 1.0 / Ranged 0.0 / Magic 0.0
//   Ex: skill hybride         → Melee 0.7 / Ranged 0.0 / Magic 0.3
//   Ex: projectile magique    → Melee 0.0 / Ranged 0.3 / Magic 0.7
//
// ⚠ Les skills permanents (bonus stats définitifs) utilisent PermanentSkillData,
//   un SO séparé — pas SkillData. SkillType.Permanent a été supprimé.
// =============================================================

[CreateAssetMenu(fileName = "skl_", menuName = "AetherTree/Skills/SkillData")]
public class SkillData : ScriptableObject
{
    // ── ① Identité ────────────────────────────────────────────
    [Header("① Identité")]
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"skl_\"\n" +
             "(ex: \"skl_shortsword_combo3\"). Ne JAMAIS afficher au joueur — voir skillName pour l'affichage.")]
    public string           skillID;
    [Tooltip("Nom affiché au joueur (fr/en). Ne jamais utiliser dans un log/comparaison —\n" +
             "utiliser le nom d'asset Unity (this.name, déjà la clé stable utilisée par\n" +
             "SaveSystem.FindSOByName) pour ça.")]
    public LocalizedText   skillName   = new LocalizedText();
    [Tooltip("Description affichée au joueur (fr/en).")]
    public LocalizedText   description = new LocalizedText();
    public List<SkillTag> tags        = new List<SkillTag>();
    public SkillType      skillType   = SkillType.Active;

    // ── ② Compatibilité arme ──────────────────────────────────
    [Header("② Compatibilité arme")]
    [Tooltip("Types d'armes compatibles — vide = universel")]
    public List<WeaponType> compatibleWeapons = new List<WeaponType>();

    [Tooltip("Skills équivalents pour d'autres variantes d'arme de la même famille — même compétence " +
             "conceptuelle, stats/anim propres à chaque arme. Vide = pas de variante (skill universel ou " +
             "exclusif). Rempli au fur et à mesure — évite de devoir reprendre tous les skills plus tard " +
             "quand le système de déblocage de variante sera implémenté (pas prévu pour le prototype).\n" +
             "Le lien part TOUJOURS du skill de base vers ses variantes — pas besoin de lien retour.")]
    public List<SkillVariantLink> weaponVariantLinks = new List<SkillVariantLink>();

    // ── ③ Effet principal ─────────────────────────────────────
    [Header("③ Effet principal")]
    [Tooltip("Effet principal du skill.\n" +
             "Damage → inflige des dégâts physiques + élémentaires\n" +
             "Buff   → applique uniquement des buffs (via StatusEffects)\n" +
             "Debuff → applique uniquement des debuffs (via StatusEffects)\n" +
             "Other  → effet spécial (drain, téléport, invocation...)")]
    public SkillEffectType effectType       = SkillEffectType.Damage;
    public float           cooldown         = 1f;
    public float           castTime         = 0f;

    // ── Effet spécial (si effectType == Other) ────────────────
    [Tooltip("Effet spécial appliqué par ce skill.\nActif uniquement si effectType = Other.")]
    [ShowIf(nameof(effectType), SkillEffectType.Other, Header = "③ Effet spécial (si effectType = Other)")]
    public SkillSpecialEffect specialEffect = SkillSpecialEffect.None;

    [Tooltip("Force du déplacement pour Pull/Push/PullAoE/PushAoE/GatherAoE/Vortex.\nDistance en unités world.")]
    [ShowIf(nameof(specialEffect), SkillSpecialEffect.Pull, SkillSpecialEffect.Push, SkillSpecialEffect.PullAoE, SkillSpecialEffect.PushAoE, SkillSpecialEffect.GatherAoE, SkillSpecialEffect.Vortex)]
    public float pullPushForce = 5f;

    [Tooltip("Ratio des dégâts restitués en soin (DrainHP). Ex: 0.5 = 50% des dégâts soignés.")]
    [Range(0f, 1f)]
    [ShowIf(nameof(specialEffect), SkillSpecialEffect.DrainHP)]
    public float drainHealRatio = 0.5f;

    [Tooltip("MobData à invoquer (Summon uniquement).")]
    [ShowIf(nameof(specialEffect), SkillSpecialEffect.Summon)]
    public MobData summonMobData;

    [Tooltip("Durée de vie de l'invocation en secondes. 0 = permanent jusqu'à la mort.")]
    [ShowIf(nameof(specialEffect), SkillSpecialEffect.Summon)]
    public float summonDuration = 30f;

    // ── ③ Dégâts physiques — Damage ou Other (ex: DrainHP) uniquement ──────
    // Jamais utilisés pour Buff/Debuff (aucun calcul de dégâts ne les lit,
    // voir SkillSystem.ApplyEffectType/CalculateDamage).
    [Tooltip("Multiplicateur global sur les dégâts physiques.\n" +
             "Ex: 1.0 = dégâts normaux | 2.0 = double dégâts physiques")]
    [ShowIf(nameof(effectType), SkillEffectType.Damage, SkillEffectType.Other,
        Header = "③ Ratios de dégâts physiques (somme doit = 1.0)")]
    public float damageMultiplier = 1f;

    [Tooltip("Part des dégâts réduite par la défense Mêlée de la cible.\n" +
             "Ex: skill mêlée pur → 1.0 | skill hybride → 0.7")]
    [Range(0f, 1f)]
    [ShowIf(nameof(effectType), SkillEffectType.Damage, SkillEffectType.Other)]
    public float damageMeleeRatio  = 1f;

    [Tooltip("Part des dégâts réduite par la défense Distance de la cible.\n" +
             "Ex: projectile → 1.0 | lancer de lame → 0.5")]
    [Range(0f, 1f)]
    [ShowIf(nameof(effectType), SkillEffectType.Damage, SkillEffectType.Other)]
    public float damageRangedRatio = 0f;

    [Tooltip("Part des dégâts réduite par la défense Magique de la cible.\n" +
             "Ex: sort pur → 1.0 | skill hybride → 0.3")]
    [Range(0f, 1f)]
    [ShowIf(nameof(effectType), SkillEffectType.Damage, SkillEffectType.Other)]
    public float damageMagicRatio  = 0f;

    // ── ③ Éléments — Damage ou Other (ex: DrainHP) uniquement ─────────────
    [Tooltip("Vide = Neutre pur (pas de dégâts élémentaires)\n" +
             "1 élément = skill élémentaire\n" +
             "2+ éléments = skill combo élémentaire")]
    [ShowIf(nameof(effectType), SkillEffectType.Damage, SkillEffectType.Other, Header = "③ Éléments")]
    public List<ElementType> elements = new List<ElementType>();

    [Range(0f, 5f)]
    [Tooltip("Multiplicateur élémentaire appliqué sur les elemPoints du joueur. GDD §6.2.\n" +
             "0   = pas de dégâts élémentaires (skill purement physique)\n" +
             "1.0 = elemPoints × 1.0  (ex: 300 pts → 300 dégâts elem)\n" +
             "2.5 = elemPoints × 2.5  (ex: 300 pts → 750 dégâts elem)\n" +
             "5.0 = skill burst élémentaire maximum\n" +
             "⚠ Ignoré automatiquement si elements est vide (skill Neutre)")]
    [ShowIf(nameof(effectType), SkillEffectType.Damage, SkillEffectType.Other)]
    public float elementalMultiplier = 1f;

    // ── ④ Exécution avancée — les 3 skillType (BasicAttack/Active/Ultimate ne diffèrent QUE
    // par leur slot SkillBar, voir SkillType — aucune raison de restreindre MultiHit/Combo à
    // Active seul). Damage ou Other uniquement (jamais Buff/Debuff, pas de sens à multi-hit/
    // comboter un soin ou un buff pur avec ce mécanisme).
    [Tooltip("Normal        → exécution standard\n" +
             "MultiHit      → une activation, N hits en séquence (hitSteps)\n" +
             "ComboSequence → N appuis successifs sur le même slot (comboSteps)")]
    [ShowIf(nameof(effectType), SkillEffectType.Damage, SkillEffectType.Other,
        Header = "④ Exécution avancée")]
    public SkillExecutionType executionType = SkillExecutionType.Normal;

    [Tooltip("MultiHit uniquement — liste des hits avec leurs stats propres.\n" +
             "Chaque HitStep définit : délai, multiplicateur, élément, effets, VFX.")]
    [ShowIf(nameof(executionType), SkillExecutionType.MultiHit)]
    public List<HitStep>   hitSteps  = new List<HitStep>();

    [Tooltip("ComboSequence uniquement — liste des SkillData steps dans l'ordre.\n" +
             "Chaque step est un skill complet avec ses propres stats et icône.")]
    [ShowIf(nameof(executionType), SkillExecutionType.ComboSequence)]
    public List<SkillData> comboSteps = new List<SkillData>();

    [Tooltip("ComboSequence uniquement — durée en secondes pendant laquelle\n" +
             "le joueur peut appuyer pour continuer le combo après chaque step.\n" +
             "Expiration → CD déclenché + retour au step 0.")]
    [ShowIf(nameof(executionType), SkillExecutionType.ComboSequence)]
    public float comboWindowDuration = 2f;

    [Tooltip("ComboSequence uniquement — délai minimum en secondes entre deux steps.\n" +
             "Empêche de spammer tous les steps du combo en moins d'une seconde.\n" +
             "0 = pas de délai minimum. Réglable par combo (ex: 0.5s pour un combo rapide,\n" +
             "2s pour un combo lent/lourd).")]
    [ShowIf(nameof(executionType), SkillExecutionType.ComboSequence)]
    public float comboStepInterval = 0f;

    // ── ⑤ Coût ────────────────────────────────────────────────
    [Header("⑤ Coût")]
    [Tooltip("Coût en mana.")]
    public float manaCost = 0f;
    [Tooltip("Coût en HP.")]
    public float hpCost   = 0f;
    [Tooltip("Coût en gold.")]
    public int   goldCost = 0;

    // ── ⑥ Ciblage & Portée ────────────────────────────────────
    [Header("⑥ Ciblage & Portée")]
    public TargetType targetType      = TargetType.Target;
    public float      range           = 2.5f;
    public float      aoeRadius       = 0f;
    public float      projectileSpeed = 15f;

    [Tooltip("Demi-angle du cône EN DEGRÉS (pas un %) — ex: 45 = éventail de 90° total\n" +
             "(45° de chaque côté de la direction visée). Champ dédié — aoeRadius garde son\n" +
             "sens habituel (rayon en mètres) pour ce targetType, pas de double-emploi.")]
    [Range(1f, 180f)]
    [ShowIf(nameof(targetType), TargetType.Cone, Header = "⑥ Cône (targetType = Cone)")]
    public float coneHalfAngle = 45f;

    [Tooltip("Qui peut être touché par ce skill — AoE ou non (Target/Dash_Target y compris,\n" +
             "pas seulement les zones).\n" +
             "Enemies  → seulement les ennemis du caster (dégâts classiques)\n" +
             "Allies   → seulement les alliés du caster (soin/buff, inclut le caster lui-même\n" +
             "           s'il est dans la zone pour un AoE — ex: Purify de zone)\n" +
             "Everyone → tout le monde, sans distinction")]
    [ShowIf(nameof(targetType), TargetType.AoE_Self, TargetType.AoE_Target, TargetType.GroundTarget,
        TargetType.Cone, TargetType.Target, TargetType.Dash_Target, DisplayName = "Cible")]
    public SkillAoeFaction aoeFaction = SkillAoeFaction.Enemies;

    // ── ⑦ Zone à impact différé / Trajectoire mobile ───────────
    [Tooltip("Transforme la résolution de ce skill en zone au sol à impact différé — au lieu de\n" +
             "résoudre les dégâts immédiatement au point de résolution existant (fin d'anim / fin\n" +
             "de canalisation), une zone se plante à cet endroit et les dégâts n'appliquent qu'après\n" +
             "impactDelay secondes, à qui se trouve RÉELLEMENT dans la zone à ce moment (permet une\n" +
             "vraie fenêtre d'esquive). Non supporté avec MultiHit/ComboSequence — la vraie garde\n" +
             "contre un step de combo est côté code (SkillBar.ResolveInstant), pas ce warning seul.\n" +
             "Cone/GroundTarget/Target + isTrajectory = seule combinaison autorisée avec isTrajectory\n" +
             "(exception délibérée, voir tooltip isTrajectory) — la zone se plante au bout du\n" +
             "cône/du trajet, EN PLUS du dégât immédiat du sweep/de l'expansion (double dégât voulu\n" +
             "si une cible reste dans le rayon final). Tous les autres targetType restent\n" +
             "mutuellement exclusifs.")]
    [ShowIf(nameof(targetType), TargetType.Self, TargetType.AoE_Self, TargetType.Target,
        TargetType.GroundTarget, TargetType.AoE_Target, TargetType.Cone,
        AndField = nameof(executionType), AndValue = SkillExecutionType.Normal,
        Header = "⑦ Zone à impact différé")]
    public bool hasDelayedImpact = false;

    [Tooltip("Délai en secondes entre le plantage de la zone (résolution existante) et le premier\n" +
             "tick de dégâts.")]
    [Min(0f)]
    [ShowIf(nameof(hasDelayedImpact), true)]
    public float impactDelay = 1f;

    [Tooltip("Durée pendant laquelle la zone reste active APRÈS le premier tick (impactDelay).\n" +
             "0 = un seul tick, la zone disparaît ensuite (impact différé simple — ex: comète).\n" +
             "> 0 = zone persistante qui retick toutes les zoneTickInterval secondes pendant cette\n" +
             "durée (ex: zone de lave, pluie de glace).")]
    [Min(0f)]
    [ShowIf(nameof(hasDelayedImpact), true)]
    public float zoneDuration = 0f;

    [Tooltip("Fréquence des ticks de dégâts pendant zoneDuration. Ignoré si zoneDuration = 0.\n" +
             "Plancher réel appliqué au runtime : 0.05s (voir SkillSystem.DelayedZoneRoutine).")]
    [Min(0.05f)]
    [ShowIf(nameof(hasDelayedImpact), true)]
    public float zoneTickInterval = 1f;

    [Tooltip("Transforme la résolution de ce skill en hitbox mobile qui voyage du caster vers " +
             "une destination (au lieu de résoudre les dégâts au point de résolution existant, " +
             "une trajectoire est parcourue et touche tout ce qui se trouve sur son passage). " +
             "Distinct de hasDelayedImpact (zone FIXE une fois plantée) — mutuellement exclusif, " +
             "SAUF Cone/GroundTarget/Target (seule combinaison autorisée, voir tooltip " +
             "hasDelayedImpact) : la zone se plante alors au bout du trajet, EN PLUS du dégât " +
             "immédiat.\n" +
             "GroundTarget : voyage vers le point cliqué au sol (visé souris).\n" +
             "Target / AoE_Target : voyage vers la position de la cible AU LANCEMENT (figée, pas " +
             "de homing — si la cible bouge après coup, la trajectoire continue vers le point où " +
             "elle était, peut la manquer). Perce tout sur le trajet, sauf si stopAtFirstHit.\n" +
             "Cone : PAS un point qui voyage — un cône qui s'ÉLARGIT depuis le caster (portée 0 → " +
             "range à la vitesse projectileSpeed), angle = coneHalfAngle, visé à la souris. Chemin " +
             "de résolution séparé (TrajectoryConeRoutine), pas le sweep point-à-point des autres " +
             "cas.")]
    [ShowIf(nameof(targetType), TargetType.GroundTarget, TargetType.Target,
        TargetType.AoE_Target, TargetType.Cone,
        AndField = nameof(executionType), AndValue = SkillExecutionType.Normal,
        Header = "⑦ Trajectoire mobile")]
    public bool isTrajectory = false;

    [Tooltip("Si coché, le sweep s'arrête au PREMIER ennemi touché (comme l'ancien Skillshot —\n" +
             "tir précis). Si décoché (défaut), perce tout ce qui est sur le trajet (comme " +
             "l'ancien Direction/LineTarget). Cone non concerné — touche toujours tout l'éventail.\n" +
             "Si combiné à hasDelayedImpact, la zone différée se plante au POINT D'ARRÊT réel, pas " +
             "à la destination d'origine.")]
    [ShowIf(nameof(targetType), TargetType.Target, TargetType.GroundTarget,
        AndField = nameof(isTrajectory), AndValue = true, Header = "⑦ Arrêt au premier hit")]
    public bool stopAtFirstHit = false;

    [Tooltip("Forme de la hitbox qui voyage — ignoré pour Cone (toujours un éventail angulaire,\n" +
             "pas une forme qui voyage).\n" +
             "Sphere → ronde dans tous les axes (largeur = profondeur = aoeRadius). Cas normal —\n" +
             "         flèche fine (aoeRadius petit) ou tube large (aoeRadius grand).\n" +
             "Box    → rectangulaire, largeur (trajectoryWidth) et profondeur (trajectoryDepth)\n" +
             "         réglables indépendamment — pour un VRAI mur plat, large sans être épais\n" +
             "         (ou l'inverse), selon le VFX. Hauteur fixe (interne, généreuse) — pas de\n" +
             "         variation de hauteur nécessaire au combat (entités ~même niveau au sol).")]
    [ShowIf(nameof(targetType), TargetType.GroundTarget, TargetType.Target, TargetType.AoE_Target,
        AndField = nameof(isTrajectory), AndValue = true, Header = "⑦ Forme de trajectoire")]
    public TrajectoryShape trajectoryShape = TrajectoryShape.Sphere;

    [Tooltip("Largeur totale de la boîte (axe horizontal, perpendiculaire au trajet). Box uniquement.")]
    [Min(0.1f)]
    [ShowIf(nameof(trajectoryShape), TrajectoryShape.Box)]
    public float trajectoryWidth = 2f;

    [Tooltip("Profondeur totale de la boîte (épaisseur sur l'axe du trajet, avant/arrière) — règle\n" +
             "selon le VFX du skill (mur fin type lame de vent vs bloc épais type mur de pierre).\n" +
             "Box uniquement.")]
    [Min(0.1f)]
    [ShowIf(nameof(trajectoryShape), TrajectoryShape.Box)]
    public float trajectoryDepth = 1f;

    [Tooltip("Si coché, la zone RECALCULE sa position à chaque tick sur l'entité vivante " +
             "(Self/AoE_Self : le CASTER — ex: tourbillon qui te suit si tu te déplaces en " +
             "spinnant. Target/AoE_Target : la CIBLE — ex: corbeaux qui suivent une cible " +
             "marquée). Si décoché (défaut), la zone reste figée à la position capturée au " +
             "lancement — ex: fiole de poison lancée au sol, zone qui punit une cible qui " +
             "s'enfuit. GroundTarget : toujours figé, ce champ n'apparaît pas (pas d'entité à " +
             "suivre).")]
    [ShowIf(nameof(targetType), TargetType.Self, TargetType.AoE_Self, TargetType.Target,
        TargetType.AoE_Target, AndField = nameof(hasDelayedImpact), AndValue = true,
        Header = "⑦ Zone qui suit")]
    public bool zoneFollowsAnchor = false;

    // ── ⑧ Effets secondaires (StatusEffects SO) ───────────────
    [Header("⑧ Effets secondaires (BuffData / DebuffData + chance)")]
    [Tooltip("Effets déclenchés à l'utilisation du skill.\n" +
             "Glisse un BuffData ou DebuffData + règle la chance.\n\n" +
             "Ex: Skill Damage + Burn 30% → dégâts + chance de brûlure\n" +
             "Ex: Skill Buff pur → glisse un BuffData Haste à 100%\n" +
             "Ex: Skill Debuff pur → glisse un DebuffData Poison à 100%")]
    public List<StatusEffectEntry> statusEffects = new List<StatusEffectEntry>();

    // ── ⑨ Visuel ───────────────────────────────────────────────
    [Header("⑨ Visuel")]
    public Sprite     icon;

    [Tooltip("VFX spawné à la position du caster AU LANCEMENT (pentacle aux pieds, glow aux\n" +
             "mains...) — ne suit PAS le caster ensuite s'il bouge.\n" +
             "Optionnel — vide = pas de VFX de cast.")]
    public GameObject vfxCast;

    [Tooltip("VFX qui suit la hitbox pendant tout le déplacement d'un skill isTrajectory — suit " +
             "la position ET s'oriente selon la direction de déplacement. Actif uniquement si " +
             "isTrajectory = true. Distinct de vfxCast (spawné au lancement, ne suit pas) et de " +
             "vfxImpact (joué au moment du hit, ponctuel).")]
    [ShowIf(nameof(isTrajectory), true)]
    public GameObject vfxTrajectory;

    [Tooltip("VFX de zone/avertissement — spawné quand la zone se plante au sol (point de\n" +
             "résolution existant), reste affiché jusqu'à la détonation. Actif uniquement si\n" +
             "hasDelayedImpact = true.")]
    [ShowIf(nameof(hasDelayedImpact), true)]
    public GameObject vfxZoneMarker;

    [Tooltip("VFX joué exactement au moment où les dégâts s'appliquent réellement — immédiat pour\n" +
             "un skill sans délai (comportement historique de vfxPrefab), ou à chaque détonation\n" +
             "pour une zone à impact différé.")]
    [FormerlySerializedAs("vfxPrefab")]
    public GameObject vfxImpact;

    [Tooltip("Animation jouée par le caster à l'exécution du skill (PlayerAnimatorController.PlayAttack).\n" +
             "Vide = pas d'animation dédiée (ex: buff pur, effet purement passif).\n" +
             "⚠ Donnée visuelle — si SpellData est un jour séparé en gameplay/visuel pour le\n" +
             "serveur-autoritaire (voir a implémenter/AetherTree_Recap_Reseau+refactor.md §Priorité 1),\n" +
             "ce champ part du côté visuel, pas gameplay.")]
    public AnimationClip attackAnimation;

    [Tooltip("Animation jouée PENDANT la canalisation (castTime > 0) — boucle ou étirée sur\n" +
             "castTime secondes. Distincte de attackAnimation (jouée sur les skills castTime 0).\n" +
             "Coupée net si la canalisation est interrompue (CC/Silence/mouvement).")]
    public AnimationClip channelAnimation;

    // ── ⑩ Son ──────────────────────────────────────────────────
    [Header("⑩ Son")]
    public AudioClip  soundEffect;

    // ── Helpers ───────────────────────────────────────────────

    /// <summary>True si le skill n'a aucun élément — pas de dégâts élémentaires.</summary>
    public bool IsNeutral => elements == null || elements.Count == 0;

    /// <summary>True si le skill a 2 éléments ou plus (combo élémentaire).</summary>
    public bool IsCombo => elements != null && elements.Count >= 2;

    /// <summary>Élément principal du skill. Neutral si aucun élément défini.</summary>
    public ElementType PrimaryElement => IsNeutral ? ElementType.Neutral : elements[0];

    /// <summary>
    /// Multiplicateur élémentaire effectif.
    /// Retourne 0 si le skill est Neutre — éléments vides = pas de dégâts élémentaires.
    /// </summary>
    public float EffectiveElementalMultiplier => IsNeutral ? 0f : elementalMultiplier;

    public bool HasElement(ElementType e) => !IsNeutral && elements.Contains(e);
    public bool HasTag(SkillTag t)        => tags != null && tags.Contains(t);

    public string GetElementsLabel()
    {
        if (IsNeutral) return "Neutre";
        return string.Join(" + ", elements);
    }

    public bool IsCompatibleWith(WeaponType weaponType)
    {
        return compatibleWeapons == null || compatibleWeapons.Count == 0
               || compatibleWeapons.Contains(weaponType);
    }

    /// <summary>
    /// Défense pondérée selon les ratios du skill.
    /// Ex: skill (meleeRatio=0.7, magicRatio=0.3) → meleeDefense×0.7 + magicDefense×0.3
    /// Appelé par CombatSystem — GDD §6.2.
    /// </summary>
    public float GetWeightedDefense(float meleeDefense, float rangedDefense, float magicDefense)
        => meleeDefense  * damageMeleeRatio
         + rangedDefense * damageRangedRatio
         + magicDefense  * damageMagicRatio;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(skillID))
            skillID = name;

        // MultiHit : SkillBar.LockForMultiHit() bloque l'auto-attaque pendant
        // somme(hitSteps.delay) + 0.3s — si attackAnimation dure sensiblement plus
        // longtemps, l'auto-attaque reprend la main avant la fin de l'anim et écrase
        // le state Attack partagé en plein milieu (bug vécu — voir historique du projet).
        // Avertissement plutôt que correction automatique : c'est aux delays ou à
        // l'anim d'être ajustés, pas au lock de compenser en silence.
        if (executionType == SkillExecutionType.MultiHit && attackAnimation != null
            && hitSteps != null && hitSteps.Count > 0)
        {
            float totalDelay = 0f;
            foreach (var step in hitSteps)
                totalDelay += step.delay;
            totalDelay += 0.3f;

            float animLength = attackAnimation.length;
            if (animLength - totalDelay > 0.15f)
            {
                Debug.LogWarning($"[SkillData:{name}] attackAnimation ({animLength:F2}s) dure " +
                                  $"{(animLength - totalDelay):F2}s de plus que le lock MultiHit " +
                                  $"({totalDelay:F2}s, somme des hitSteps.delay + marge) — l'auto-attaque " +
                                  $"va reprendre la main avant la fin de l'animation. Allonge les delays " +
                                  $"des hitSteps, ou raccourcis/retime l'animation.", this);
            }
        }

        // Une canalisation (castTime > 0) combinée à MultiHit/ComboSequence n'est pas gérée —
        // TryAdvanceCombo() (SkillBar) prend la main avant le dispatch castTime, castTime est
        // silencieusement ignoré. Avertit plutôt que de laisser un designer se demander
        // pourquoi son skill ne canalise pas.
        if (castTime > 0f && executionType != SkillExecutionType.Normal)
            Debug.LogWarning($"[SkillData:{name}] castTime > 0 avec executionType = {executionType} — " +
                              "combinaison non recommandée. Avec ComboSequence, le combo prend la " +
                              "main et castTime est totalement ignoré (le skill ne canalise jamais). " +
                              "Avec MultiHit, le skill canalise normalement puis exécute sa séquence " +
                              "de hits à la résolution, MAIS la convention de cooldown différé du " +
                              "MultiHit n'est pas respectée dans ce cas (le CD de canalisation prend " +
                              "le dessus). Remets executionType à Normal si ce skill doit canaliser " +
                              "proprement.", this);

        // hasDelayedImpact + MultiHit/ComboSequence sur CE skill LUI-MÊME est détectable ici —
        // mais un step de combo (comboSteps[i] d'un AUTRE SkillData) a lui-même
        // executionType = Normal, donc invisible à ce check. La vraie garde contre ce cas
        // précis vit dans SkillBar.ResolveInstant() (garde combo évaluée avant tout
        // branchement zone différée) — ce warning reste utile pour la saisie directe sur un
        // skill MultiHit/ComboSequence, pas comme protection complète.
        if (hasDelayedImpact && executionType != SkillExecutionType.Normal)
            Debug.LogWarning($"[SkillData:{name}] hasDelayedImpact = true avec executionType = " +
                              $"{executionType} — combinaison non gérée, la zone différée ne " +
                              "fonctionne qu'avec executionType = Normal (instant ou canalisé).", this);

        // [ShowIf] masque hasDelayedImpact hors Self/AoE_Self/Target/GroundTarget/AoE_Target/Cone dans
        // l'Inspector, mais ne le RESET jamais si targetType change après coup (ShowIf n'a aucun
        // writeback) — le champ reste true, invisible, et PlantDelayedZone() tourne quand même au
        // runtime, repliant silencieusement le skill sur une sphère centrée sur le caster.
        bool hasDelayedImpactSupportedTargetType =
            targetType == TargetType.Self || targetType == TargetType.AoE_Self ||
            targetType == TargetType.Target || targetType == TargetType.GroundTarget ||
            targetType == TargetType.AoE_Target || targetType == TargetType.Cone;
        if (hasDelayedImpact && !hasDelayedImpactSupportedTargetType)
            Debug.LogWarning($"[SkillData:{name}] hasDelayedImpact = true avec targetType = " +
                              $"{targetType} — non supporté (le champ est masqué dans l'Inspector " +
                              "mais reste actif). La zone se plantera quand même, centrée sur le " +
                              "caster. Décoche hasDelayedImpact ou remets targetType sur Self/" +
                              "AoE_Self/Target/GroundTarget/AoE_Target/Cone.", this);

        // hasDelayedImpact réutilise aoeRadius pour la taille de la zone (Physics.OverlapSphere) —
        // mais aoeRadius vaut 0 par défaut et n'est jamais lu par le chemin normal d'un skill
        // targetType = Target (ExecuteOnTarget ne s'en sert pas). Sans avertissement, le cas
        // d'usage principal du chantier (comète sur Target) plante une zone de rayon 0 — touche
        // uniquement une cible parfaitement immobile, "rate" au moindre mouvement, sans qu'on
        // comprenne pourquoi.
        if (hasDelayedImpact && aoeRadius <= 0f)
            Debug.LogWarning($"[SkillData:{name}] hasDelayedImpact = true avec aoeRadius = 0 — " +
                              "la zone différée réutilise aoeRadius pour sa taille (contrairement " +
                              "à un skill Target normal, qui l'ignore). Règle aoeRadius > 0, sinon " +
                              "la zone ne touche qu'une cible parfaitement immobile au point exact.", this);

        // isTrajectory et hasDelayedImpact sont mutuellement exclusifs, SAUF Cone/GroundTarget/
        // Target — exception délibérée (demande Florian) : le sweep/l'expansion inflige son
        // dégât immédiat ET une zone classique se plante en plus au bout du trajet (voir
        // SkillSystem.TrajectoryRoutine/TrajectoryConeRoutine — ex: mur de feu qui voyage vers le
        // point cliqué (GroundTarget) et laisse une zone brûlante à l'arrivée). Pour tout autre
        // targetType, toujours incompatibles — un skill est soit une zone fixe différée, soit une
        // trajectoire mobile. Avertissement seulement, pas de correction automatique (idiome du
        // fichier).
        bool trajectoryPlusZoneSupportedTargetType =
            targetType == TargetType.Cone || targetType == TargetType.GroundTarget ||
            targetType == TargetType.Target;
        if (isTrajectory && hasDelayedImpact && !trajectoryPlusZoneSupportedTargetType)
            Debug.LogWarning($"[SkillData:{name}] isTrajectory = true ET hasDelayedImpact = " +
                              "true simultanément avec targetType = " + targetType + " — combinaison " +
                              "non supportée pour ce targetType (seuls Cone/GroundTarget/Target " +
                              "l'autorisent). Décoche l'un des deux.", this);

        // [ShowIf] masque isTrajectory hors GroundTarget/Target/AoE_Target/Cone dans l'Inspector,
        // mais ne le RESET jamais si targetType change après coup (ShowIf n'a aucun writeback) —
        // le champ reste true, invisible, et StartTrajectory() tourne quand même au runtime,
        // traitant silencieusement le skill comme le trajet générique (voir SkillSystem.StartTrajectory).
        bool isTrajectorySupportedTargetType =
            targetType == TargetType.GroundTarget || targetType == TargetType.Target ||
            targetType == TargetType.AoE_Target   || targetType == TargetType.Cone;
        if (isTrajectory && !isTrajectorySupportedTargetType)
            Debug.LogWarning($"[SkillData:{name}] isTrajectory = true avec targetType = " +
                              $"{targetType} — non supporté (le champ est masqué dans l'Inspector " +
                              "mais reste actif). La trajectoire sera quand même lancée, traitée " +
                              "comme le trajet générique (voir StartTrajectory). Décoche isTrajectory " +
                              "ou remets targetType sur GroundTarget/Target/AoE_Target/Cone.", this);

        // Symétrique du warning existant hasDelayedImpact && executionType != Normal. Sans lui,
        // un skill isTrajectory configuré en MultiHit/ComboSequence n'a aucun avertissement alors
        // que StartTrajectory() n'est jamais atteinte depuis le dispatch MultiHit/ComboSequence
        // normal (silencieusement ignorée) — sauf le cas particulier castTime > 0 + MultiHit, où
        // LaunchSkill() teste castTime AVANT executionType et route vers StartChannel()/
        // ResolveChannel() (isTrajectory y EST atteint, hitSteps silencieusement ignoré) —
        // comportement préexistant identique pour hasDelayedImpact, non corrigé ici.
        if (isTrajectory && executionType != SkillExecutionType.Normal)
            Debug.LogWarning($"[SkillData:{name}] isTrajectory = true avec executionType = " +
                              $"{executionType} — combinaison non gérée, la trajectoire ne " +
                              "fonctionne qu'avec executionType = Normal (instant ou canalisé).", this);
    }
#endif
}

// =============================================================
// ENUMS
// =============================================================

/// <summary>
/// Type de skill — détermine l'onglet dans SkillLibraryUI et les règles d'équipement.
/// ⚠ Permanent supprimé — les passifs définitifs utilisent PermanentSkillData (SO séparé).
/// ⚠ PassiveUtility supprimé — vestige jamais fini (aucun moteur runtime), remplacé
/// intégralement par PassiveSkillData (SO séparé, slots P1/P2/P3 PassifBar, GDD §7.5).
/// </summary>
public enum SkillType
{
    BasicAttack = 0, // Attaque de base — slot 0 SkillBar uniquement
    Active      = 1, // Sort actif — slots 1-8 SkillBar
    Ultimate    = 2, // Ultime — slot 9 SkillBar
}

public enum SkillEffectType
{
    Damage = 0,  // Inflige des dégâts physiques + élémentaires
    Buff   = 1,  // Applique uniquement des buffs (via StatusEffects SO)
    Debuff = 2,  // Applique uniquement des debuffs (via StatusEffects SO)
    Other  = 3,  // Effet spécial (drain, téléport, invocation, dash...)
}

public enum ModifierType  { Flat = 0, Percent = 1 }

public enum TargetType
{
    Target = 0, Self = 1, AoE_Self = 2, AoE_Target = 3,
    GroundTarget = 4, Cone = 5, Dash_Target = 6, Dash_Direction = 7,
}

/// <summary>Forme de la hitbox mobile d'un skill isTrajectory — voir SkillData.trajectoryShape.
/// Ignoré pour Cone (toujours angulaire, jamais une forme qui voyage).</summary>
public enum TrajectoryShape
{
    Sphere = 0, // Ronde dans tous les axes (largeur = hauteur = aoeRadius) — cas par défaut.
    Box    = 1, // Rectangulaire, largeur/profondeur indépendantes (trajectoryWidth/trajectoryDepth), hauteur fixe.
}

/// <summary>Qui est touché par un effet de zone/multi-cibles — voir SkillSystem.IsAlly.</summary>
public enum SkillAoeFaction
{
    Enemies  = 0,   // Ne touche que les ennemis du caster (comportement historique implicite)
    Allies   = 1,   // Ne touche que les alliés du caster (inclut le caster s'il est dans la zone)
    Everyone = 2,   // Touche tout le monde dans la zone, sans distinction
}

// =============================================================
// SKILL SPECIAL EFFECTS
// =============================================================
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

public enum SkillTag
{
    Illusion = 0, Invocateur = 1, Berserker = 2, Necromancien = 3, Furtif = 4, Duelliste = 5,
    Soutien = 6, Mobilite = 7, Zone = 8,
    Buff = 9, Debuff = 10, DoT = 11, Bleed = 12, Stun = 13, Root = 14, Knockback = 15, Shield = 16, Drain = 17, Combo = 18,
    BasicAttack = 19, HeavyAttack = 20, RangedAttack = 21, MagicAttack = 22,
}

// =============================================================
// SKILL EXECUTION TYPE
// =============================================================
public enum SkillExecutionType
{
    Normal        = 0,  // Comportement standard — aucun changement
    MultiHit      = 1,  // Une activation, N hits en séquence (SkillData.hitSteps)
    ComboSequence = 2,  // N appuis successifs sur le même slot (SkillData.comboSteps)
}

// =============================================================
// HIT STEP — un hit dans un MultiHit
// Chaque step a ses propres stats indépendantes du SkillData parent.
// =============================================================
[System.Serializable]
public class HitStep
{
    [Header("Identification")]
    public string stepName = "Hit";

    [Tooltip("Délai avant ce hit en secondes (depuis le hit précédent ou l'activation).")]
    [Min(0f)]
    public float delay = 0.3f;

    [Header("Dégâts physiques")]
    public float damageMultiplier  = 1f;
    [Range(0f, 1f)] public float damageMeleeRatio  = 1f;
    [Range(0f, 1f)] public float damageRangedRatio = 0f;
    [Range(0f, 1f)] public float damageMagicRatio  = 0f;

    [Header("Élémentaire")]
    [Tooltip("Élément de ce hit. Neutral = pas de dégâts élémentaires sur ce hit.")]
    public ElementType element             = ElementType.Neutral;

    [Range(0f, 5f)]
    [Tooltip("Multiplicateur élémentaire de ce hit.\n" +
             "0 = pas de dégâts élémentaires | 1.0 = elemPoints × 1.0\n" +
             "⚠ Ignoré si element = Neutral")]
    public float       elementalMultiplier = 0f;

    [Header("Effets de statut — appliqués sur ce hit uniquement")]
    public List<StatusEffectEntry> statusEffects = new List<StatusEffectEntry>();

    [Header("VFX / Son (optionnel — utilise celui du skill parent si vide)")]
    public GameObject vfxPrefab;
    public AudioClip  soundEffect;

    /// <summary>Multiplicateur élémentaire effectif — 0 si élément Neutral.</summary>
    public float EffectiveElementalMultiplier
        => element == ElementType.Neutral ? 0f : elementalMultiplier;
}

// =============================================================
// SKILL VARIANT LINK — lien vers l'équivalent d'un skill pour une
// autre variante d'arme de la même famille (ex: skl_massue_frappe1
// → { Hammer, skl_marteau_frappe1 }). Système de déblocage pas
// encore implémenté (pas prévu pour le prototype) — ce champ existe
// pour être rempli au fur et à mesure et éviter une reprise de tous
// les skills plus tard.
// =============================================================
[System.Serializable]
public class SkillVariantLink
{
    public WeaponType weaponType;
    public SkillData  skill;
}
