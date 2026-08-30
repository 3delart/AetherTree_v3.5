using UnityEngine;

// =============================================================
// DAMAGECHECKER.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Checkers/DamageChecker.cs
//
// Ecoute : DamageDealtEvent
//
// Couvre :
//   - Montant min, élément, crit, one-hit
//   - Dégâts infligés (caster) OU reçus (target)
//   - HP restant après le coup (ex : survivre avec 1 HP)
//   - Affinité + rang élémentaire du caster au moment du coup
//   - Affinité + rang élémentaire de la cible (si c'est un joueur)
//
// Exemples d'usage :
//   "Infliger 12 500 dégâts Feu en one-hit en étant rang 5 Feu dominant"
//     → isReceived=false, element=Fire, inOneHit=true, minAmount=12500,
//       casterAffinityElement=Fire, casterAffinityRankMin=5, casterMustBeDominant=true
//
//   "Survivre à un coup qui nous laisse à 1 HP"
//     → isReceived=true, maxRemainingHP=1
//
//   "Recevoir 30 sorts élémentaires Feu"
//     → isReceived=true, element=Fire  (countRequired=30 sur ConditionEntry)
// =============================================================

[System.Serializable]
public class DamageChecker : ConditionCheckerBase
{
    [Header("Dégâts")]
    [Tooltip("0 = pas de minimum")]
    public float       minAmount  = 0f;
    [Tooltip("Any = n'importe quel élément")]
    public ElementType element    = ElementType.Any;
    [Tooltip("False = dégâts infligés par le joueur | True = dégâts reçus par le joueur")]
    public bool        isReceived = false;
    public bool        mustBeCrit = false;
    public bool        inOneHit   = false;

    [Header("HP restant après le coup (reçu uniquement)")]
    [Tooltip("0 = pas de restriction. Ex : 1 = survivre avec exactement 1 HP.")]
    public int maxRemainingHP = 0;

    [Header("Affinité du caster au moment du coup")]
    [Tooltip("Any = pas de restriction. Ignoré si element = Any.")]
    public ElementType casterAffinityElement  = ElementType.Any;
    [Tooltip("0 = pas de rang minimum")]
    public int         casterAffinityRankMin  = 0;
    [Tooltip("True = cet élément doit être l'élément dominant du caster")]
    public bool        casterMustBeDominant   = false;

    [Header("Affinité de la cible au moment du coup (si c'est un joueur)")]
    [Tooltip("Any = pas de restriction")]
    public ElementType targetAffinityElement  = ElementType.Any;
    [Tooltip("0 = pas de rang minimum")]
    public int         targetAffinityRankMin  = 0;
    [Tooltip("True = cet élément doit être l'élément dominant de la cible")]
    public bool        targetMustBeDominant   = false;

    public override bool Evaluate(object gameEvent, Player player)
    {
        if (gameEvent is not DamageDealtEvent e) return false;

        // ── Dégâts de base ────────────────────────────────────
        if (e.amount < minAmount)                                   return false;
        if (element != ElementType.Any && e.element != element)    return false;
        if (mustBeCrit && !e.isCrit)                               return false;
        if (inOneHit   && !e.isOneHit)                             return false;

        // ── Direction : infligé ou reçu ───────────────────────
        bool playerIsTarget = (e.target == player);
        if (isReceived != playerIsTarget)                           return false;

        // ── HP restant après le coup (reçu uniquement) ────────
        if (isReceived && maxRemainingHP > 0 && player != null)
        {
            if (player.CurrentHP > maxRemainingHP)                 return false;
            if (player.CurrentHP <= 0)                             return false; // mort = pas survécu
        }

        // ── Affinité du caster ────────────────────────────────
        // Vérifié sur le joueur si isReceived=false, sinon sur la source
        if (casterAffinityElement != ElementType.Any || casterAffinityRankMin > 0 || casterMustBeDominant)
        {
            // Le "caster" est le joueur quand isReceived=false,
            // ou la source (si c'est un joueur) quand isReceived=true
            var casterPlayer = isReceived
                ? e.source as Player
                : player;

            var elemental = casterPlayer?.GetComponent<ElementalSystem>();
            if (elemental == null) return false;

            var checkElem = casterAffinityElement != ElementType.Any ? casterAffinityElement : element;
            if (casterMustBeDominant   && elemental.GetDominantElement()          != checkElem)            return false;
            if (casterAffinityRankMin > 0 && elemental.GetElementRank(checkElem) < casterAffinityRankMin) return false;
        }

        // ── Affinité de la cible ──────────────────────────────
        if (targetAffinityElement != ElementType.Any || targetAffinityRankMin > 0 || targetMustBeDominant)
        {
            var targetPlayer = e.target as Player;
            var elemental    = targetPlayer?.GetComponent<ElementalSystem>();
            if (elemental == null) return false;

            var checkElem = targetAffinityElement != ElementType.Any ? targetAffinityElement : element;
            if (targetMustBeDominant   && elemental.GetDominantElement()          != checkElem)            return false;
            if (targetAffinityRankMin > 0 && elemental.GetElementRank(checkElem) < targetAffinityRankMin) return false;
        }

        return true;
    }
}
