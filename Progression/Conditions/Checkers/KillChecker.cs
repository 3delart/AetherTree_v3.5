using UnityEngine;

// =============================================================
// KILLCHECKER.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Checkers/KillChecker.cs
//
// Ecoute : MobKilledEvent
//
// Couvre :
//   - Mob spécifique / élément / boss / PvP
//   - Arme & skill (élément du skill)
//   - Etat du joueur au moment du kill :
//       affinité élémentaire + rang (vérifié en temps réel)
//       stealth, unarmed, solo, groupe, low HP, nuit
//   - Zone
//
// Note "First Kill" :
//   Pour un world first, utilise ce checker avec
//   maxWinners = 1 et scope = Server sur la ConditionEntry.
//   Pas besoin d'un FirstKillChecker séparé.
// =============================================================

[System.Serializable]
public class KillChecker : ConditionCheckerBase
{
    [Header("Cible")]
    [Tooltip("null = n'importe quel mob ou joueur")]
    public MobData     specificMob      = null;
    [Tooltip("Any = n'importe quel élément du mob")]
    public ElementType mobElement       = ElementType.Any;
    public bool        mustBeBoss       = false;
    [Tooltip("True = la cible doit être un joueur (PvP kill)")]
    public bool        isPlayerKill     = false;

    [Header("Arme & Skill")]
    [Tooltip("Any = n'importe quelle arme")]
    public WeaponType  weapon           = WeaponType.Any;
    [Tooltip("null = n'importe quel skill pour le coup fatal")]
    public SkillData   requiredSkill    = null;
    [Tooltip("Any = n'importe quel élément du skill fatal")]
    public ElementType skillElement     = ElementType.Any;

    [Header("Affinité du joueur au moment du kill")]
    [Tooltip("Any = pas de restriction d'élément d'affinité")]
    public ElementType requiredAffinityElement = ElementType.Any;
    [Tooltip("0 = pas de rang minimum requis")]
    public int         affinityRankMin         = 0;
    [Tooltip("True = cet élément doit être l'élément dominant du joueur")]
    public bool        affinityMustBeDominant  = false;

    [Header("État du joueur au moment du kill")]
    [Tooltip("Any = pas de restriction sur l'élément dominant (indépendant de l'affinité)")]
    public ElementType dominantElement  = ElementType.Any;
    public bool        mustBeStealth    = false;
    public bool        mustBeUnarmed    = false;
    public bool        mustBeSolo       = false;
    public bool        mustBeInGroup    = false;
    [Tooltip("True = le joueur doit avoir moins de lowHPThreshold % de HP max au moment du kill")]
    public bool        lowHP            = false;
    [Tooltip("Seuil HP en % pour lowHP. 0.2 = 20%, 0.1 = 10%, etc. Ignoré si lowHP est false.")]
    [Range(0.01f, 0.99f)]
    public float       lowHPThreshold   = 0.20f;
    public bool        atNight          = false;

    [Header("Zone")]
    [Tooltip("Vide = n'importe quelle zone")]
    public string inZone = "";

    public override bool Evaluate(object gameEvent, Player player)
    {
        if (gameEvent is not MobKilledEvent e) return false;

        // ── Cible ────────────────────────────────────────────
        if (specificMob  != null && e.mob != specificMob)                        return false;
        if (mobElement   != ElementType.Any && e.mob?.elementType != mobElement) return false;
        if (mustBeBoss   && !e.wasBoss)                                          return false;
        if (isPlayerKill  &&  e.mob != null)                                     return false; // PvP = pas de MobData
        if (!isPlayerKill &&  e.mob == null)                                     return false; // PvE = mob requis

        // ── Arme & skill ─────────────────────────────────────
        if (weapon        != WeaponType.Any && e.killerWeapon != weapon)                         return false;
        if (requiredSkill != null           && e.killerSkill  != requiredSkill)                  return false;
        if (skillElement  != ElementType.Any && e.killerSkill?.PrimaryElement != skillElement)   return false;

        // ── Affinité joueur au moment du kill ─────────────────
        if (requiredAffinityElement != ElementType.Any || affinityRankMin > 0 || affinityMustBeDominant)
        {
            var elemental = player?.GetComponent<ElementalSystem>();
            if (elemental == null) return false;

            var checkElement = requiredAffinityElement != ElementType.Any
                ? requiredAffinityElement
                : dominantElement; // fallback si element Any mais dominant requis

            // Guard : si aucun élément cible n'a été précisé (checkElement == Any),
            // le check dominant n'a pas de sens — on le saute plutôt que de bloquer
            // indéfiniment (GetDominantElement() ne retourne jamais Any).
            if (affinityMustBeDominant && checkElement != ElementType.Any
                && elemental.GetDominantElement() != checkElement)           return false;
            if (affinityRankMin > 0    && checkElement != ElementType.Any
                && elemental.GetElementRank(checkElement) < affinityRankMin) return false;
        }

        // ── État joueur ───────────────────────────────────────
        if (dominantElement != ElementType.Any)
        {
            var elemental = player?.GetComponent<ElementalSystem>();
            if (elemental == null || elemental.GetDominantElement() != dominantElement) return false;
        }
        if (mustBeStealth && !e.wasStealth)                                                              return false;
        if (mustBeUnarmed && !e.wasUnarmed)                                                              return false;
        if (mustBeSolo    &&  e.isInParty)                                                               return false;
        if (mustBeInGroup && !e.isInParty)                                                               return false;
        if (atNight       && !(DayNightCycle.Instance?.IsNight ?? false))                                return false;
        if (lowHP         && player != null && player.CurrentHP / player.MaxHP >= lowHPThreshold)        return false;

        // ── Zone ─────────────────────────────────────────────
        if (!string.IsNullOrEmpty(inZone) && e.locationID != inZone) return false;

        return true;
    }
}
