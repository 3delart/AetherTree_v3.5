using UnityEngine;

// =============================================================
// STATPOINTSYSTEM — Système de points de stats libres
// Path : Assets/Scripts/Core/StatPointSystem.cs
// AetherTree GDD v3.5 — §3.2.1
//
// Pool de 300 points totaux (lv1→lv100, +3 pts/niveau).
// 4 stats investissables : Attaque, Défense, Élémentaire, HP.
// Cap à 100 rangs par stat — impossible de maxer deux stats simultanément.
//
// Courbe de coût progressive :
//   Rangs  1-20 : 1 pt/rang  → 20 pts cumulés
//   Rangs 21-40 : 2 pts/rang → 60 pts cumulés
//   Rangs 41-60 : 3 pts/rang → 120 pts cumulés
//   Rangs 61-80 : 4 pts/rang → 200 pts cumulés
//   Rangs 81-100: 5 pts/rang → 300 pts cumulés
//
// Paliers de bonus — débloqués cumulativement à chaque rang multiple de 10.
// Les bonus s'accumulent : rang 40 Attaque = bonus rang10 + rang20 + rang30 + rang40.
//
// Bonus linéaires — chaque rang investi donne immédiatement :
//   Attaque    : +1 Attaque flat
//   Défense    : +1 Défense flat (×3 types : mêlée, distance, magie)
//   Élémentaire: +1 Pts élémentaire (all)
//   HP         : +10 HP max + +10 MP max
//
// Respec : reset total via PNJ dédié (RespecSystem) — coût Gold élevé, illimité.
// Les bonus de paliers sont perdus jusqu'à réinvestissement.
//
// Flow :
//   OnLevelUp(n) → GainPoints(3) → points disponibles augmentent
//   InvestPoint(stat) → coût calculé → rang++ → ApplyMilestoneIfReached() → RecalculateStats()
//   Respec() → tous rangs à 0 → RecalculateStats()
// =============================================================

[RequireComponent(typeof(Player))]
public class StatPointSystem : MonoBehaviour
{
    // =========================================================
    // RANGS ACTUELS — [0..100] par stat
    // =========================================================

    public int rankAttack    { get; private set; } = 0;
    public int rankDefense   { get; private set; } = 0;
    public int rankElemental { get; private set; } = 0;
    public int rankHP        { get; private set; } = 0;

    // =========================================================
    // POINTS
    // =========================================================

    /// <summary>Points disponibles à investir.</summary>
    public int availablePoints { get; private set; } = 0;

    /// <summary>Points totaux accumulés depuis lv1 (3 pts × niveau actuel).</summary>
    public int totalPointsEarned { get; private set; } = 0;

    // =========================================================
    // BONUS LINÉAIRES — 1 rang = 1 de stat, appliqués à chaque rang investi
    // +1 Attaque / +1 Défense (×3 types) / +1 Pts élémentaire (all) / +10 HP + 10 MP
    // S'ajoutent aux bonus de paliers dans CharacterStats.RecalculateStats()
    // =========================================================

    public float linearBonusAttack    => rankAttack    * 1f;
    public float linearBonusDefense   => rankDefense   * 1f;
    public float linearBonusElemental => rankElemental * 1f;
    public float linearBonusHP        => rankHP        * 10f;
    public float linearBonusMP        => rankHP        * 10f;

    // =========================================================
    // BONUS ACTIFS — résultat de tous les paliers débloqués
    // Lus par CharacterStats.RecalculateStats() pour pousser sur Entity.
    // =========================================================

    // ── Attaque ───────────────────────────────────────────────
    public float bonusAttackFlat  { get; private set; } = 0f;  // +Attaque flat
    public float bonusPrecision   { get; private set; } = 0f;  // +Précision flat
    public float bonusCritChance  { get; private set; } = 0f;  // +CritChance [0..1]
    public float bonusCritMultiplier  { get; private set; } = 0f;  // +CritDamage multiplicateur

    // ── Défense ───────────────────────────────────────────────
    public float bonusDefenseFlat      { get; private set; } = 0f;  // +Défense flat (×3 types)
    public float bonusDodge            { get; private set; } = 0f;  // +Dodge flat
    public float bonusCritDmgReduction { get; private set; } = 0f;  // Réduction dégâts critiques reçus [0..1]
    public float bonusResistAllDef     { get; private set; } = 0f;  // +Résist. élémentaire all (depuis Défense)

    // ── Élémentaire ───────────────────────────────────────────
    public float bonusElementalPoints    { get; private set; } = 0f;  // +Pts élémentaires all
    public float bonusCooldownReduction  { get; private set; } = 0f;  // Réduction CD [0..1]
    public float bonusResistAllEle       { get; private set; } = 0f;  // +Résist. élémentaire all (depuis Élémentaire)

    // ── HP / MP ───────────────────────────────────────────────
    public float bonusMaxHP   { get; private set; } = 0f;
    public float bonusMaxMP   { get; private set; } = 0f;  // Poussé sur Entity.maxMana via RecalculateStats()
    public float bonusRegenHP { get; private set; } = 0f;
    public float bonusRegenMP { get; private set; } = 0f;

    // =========================================================
    // RÉFÉRENCE PLAYER
    // =========================================================

    private Player _player;

    private void Awake()
    {
        _player = GetComponent<Player>();
    }

    // =========================================================
    // GAIN DE POINTS — appelé par Player.OnLevelUp()
    // =========================================================

    /// <summary>
    /// Ajoute des points disponibles. Appelé à chaque level up (+3 pts/niveau).
    /// </summary>
    public void GainPoints(int amount)
    {
        availablePoints   += amount;
        totalPointsEarned += amount;
    }

    // =========================================================
    // INVESTISSEMENT
    // =========================================================

    /// <summary>
    /// Tente d'investir un point dans la stat choisie.
    /// Retourne true si l'investissement a réussi.
    /// </summary>
    public bool InvestPoint(StatCategory stat)
    {
        int currentRank = GetRank(stat);
        if (currentRank >= 100) return false;

        int cost = GetCostForNextRank(currentRank);
        if (availablePoints < cost) return false;

        availablePoints -= cost;
        SetRank(stat, currentRank + 1);

        // Applique le palier si rang multiple de 10
        if (GetRank(stat) % 10 == 0)
            ApplyMilestone(stat, GetRank(stat));

        _player.stats.RecalculateStats(_player);
        return true;
    }

    /// <summary>
    /// Investit plusieurs points d'un coup (raccourci UI).
    /// S'arrête si plus de points disponibles ou rang 100 atteint.
    /// </summary>
    public void InvestPoints(StatCategory stat, int count)
    {
        for (int i = 0; i < count; i++)
            if (!InvestPoint(stat)) break;
    }

    // =========================================================
    // RESPEC — reset total
    // =========================================================

    /// <summary>
    /// Réinitialise tous les rangs et récupère tous les points investis.
    /// Appelé par RespecSystem après paiement. GDD v3.5 §3.2.1.
    /// Disponible chez le PNJ de Respec dans chaque ville principale — illimité, coût Gold élevé.
    /// </summary>
    public void Respec()
    {
        availablePoints = totalPointsEarned;

        rankAttack    = 0;
        rankDefense   = 0;
        rankElemental = 0;
        rankHP        = 0;

        ResetBonuses();
        _player.stats.RecalculateStats(_player);

        Debug.Log("[STATPOINTS] Respec effectué — tous les points récupérés.");
    }

    // =========================================================
    // CHARGEMENT SAUVEGARDE
    // =========================================================

    /// <summary>
    /// Restaure l'état StatPoints depuis une sauvegarde : rangs investis + pool de points.
    /// Recalcule les bonus de paliers puis pousse sur Entity via RecalculateStats(). GDD §3.2.1.
    /// À appeler APRÈS Player.OnLevelUp() au chargement (écrase le pool donné par le level-up).
    /// </summary>
    public void LoadFromSave(int atk, int def, int ele, int hp, int totalEarned, int available)
    {
        rankAttack    = Mathf.Clamp(atk, 0, 100);
        rankDefense   = Mathf.Clamp(def, 0, 100);
        rankElemental = Mathf.Clamp(ele, 0, 100);
        rankHP        = Mathf.Clamp(hp,  0, 100);
        totalPointsEarned = Mathf.Max(0, totalEarned);
        availablePoints   = Mathf.Max(0, available);

        // Filet de sécurité — si le total sauvegardé est incohérent avec le niveau chargé
        // (save éditée à la main, ou tout désync futur), on complète au lieu d'écraser
        // silencieusement le pool attendu. GDD §3.2.1 — 3 pts/niveau.
        if (_player != null)
        {
            int expectedTotal = _player.level * 3;
            if (expectedTotal > totalPointsEarned)
            {
                int missing = expectedTotal - totalPointsEarned;
                totalPointsEarned += missing;
                availablePoints   += missing;
            }
        }

        RecalculateAllBonuses();
        if (_player != null) _player.stats.RecalculateStats(_player);
    }

    // =========================================================
    // COÛT PAR RANG
    // =========================================================

    /// <summary>
    /// Coût en points pour passer du rang actuel au rang suivant.
    /// Rangs  1-20 : 1 pt | 21-40 : 2 pts | 41-60 : 3 pts | 61-80 : 4 pts | 81-100 : 5 pts
    /// </summary>
    public static int GetCostForNextRank(int currentRank)
    {
        if (currentRank < 20) return 1;
        if (currentRank < 40) return 2;
        if (currentRank < 60) return 3;
        if (currentRank < 80) return 4;
        return 5;
    }

    /// <summary>Coût total pour atteindre un rang cible depuis le rang 0.</summary>
    public static int GetCumulativeCost(int targetRank)
    {
        int total = 0;
        for (int r = 0; r < targetRank; r++)
            total += GetCostForNextRank(r);
        return total;
    }

    // =========================================================
    // PALIERS DE BONUS — GDD v3.5 §3.2.1
    // Appliqués cumulativement — un palier n'est jamais "remplacé".
    // =========================================================

    private void ApplyMilestone(StatCategory stat, int rank)
    {
        switch (stat)
        {
            case StatCategory.Attack:    ApplyAttackMilestone(rank);    break;
            case StatCategory.Defense:   ApplyDefenseMilestone(rank);   break;
            case StatCategory.Elemental: ApplyElementalMilestone(rank); break;
            case StatCategory.HP:        ApplyHPMilestone(rank);        break;
        }
    }

    // ── Attaque ───────────────────────────────────────────────
    private void ApplyAttackMilestone(int rank)
    {
        switch (rank)
        {
            case 10:  bonusAttackFlat += 5f;                                         break;
            case 20:  bonusPrecision  += 10f;                                        break;
            case 30:  bonusCritChance += 0.03f;                                      break;
            case 40:  bonusAttackFlat += 5f;  bonusPrecision += 10f;                 break;
            case 50:  bonusCritMultiplier += 0.10f;                                      break;
            case 60:  bonusPrecision  += 15f;                                        break;
            case 70:  bonusAttackFlat += 10f; bonusPrecision += 15f;                 break;
            case 80:  bonusCritChance += 0.03f;                                      break;
            case 90:  bonusCritMultiplier += 0.20f;                                      break;
            case 100: bonusAttackFlat += 30f; bonusPrecision += 30f;
                      bonusCritChance += 0.08f; bonusCritMultiplier += 0.20f;            break;
        }
    }

    // ── Défense ───────────────────────────────────────────────
    private void ApplyDefenseMilestone(int rank)
    {
        switch (rank)
        {
            case 10:  bonusDefenseFlat      += 10f;                                  break;
            case 20:  bonusDodge            += 5f;                                   break;
            case 30:  bonusCritDmgReduction += 0.02f;                                break;
            case 40:  bonusDodge            += 5f;  bonusDefenseFlat += 10f;         break;
            case 50:  bonusResistAllDef     += 0.02f;                                break;
            case 60:  bonusDefenseFlat      += 20f;                                  break;
            case 70:  bonusCritDmgReduction += 0.02f;                                break;
            case 80:  bonusDodge            += 10f;                                  break;
            case 90:  bonusDefenseFlat      += 35f;                                  break;
            case 100: bonusDodge            += 30f; bonusDefenseFlat += 25f;
                      bonusResistAllDef     += 0.03f; bonusCritDmgReduction += 0.06f; break;
        }
    }

    // ── Élémentaire ───────────────────────────────────────────
    private void ApplyElementalMilestone(int rank)
    {
        switch (rank)
        {
            case 10:  bonusElementalPoints   += 5f;                                  break;
            case 20:  bonusElementalPoints   += 5f;                                  break;
            case 30:  bonusElementalPoints   += 5f;                                  break;
            case 40:  bonusCooldownReduction += 0.05f;                               break;
            case 50:  bonusResistAllEle      += 0.03f;                               break;
            case 60:  bonusElementalPoints   += 10f;                                 break;
            case 70:  bonusElementalPoints   += 10f;                                 break;
            case 80:  bonusElementalPoints   += 10f;                                 break;
            case 90:  bonusElementalPoints   += 10f;                                 break;
            case 100: bonusCooldownReduction += 0.10f; bonusElementalPoints += 25f;
                      bonusResistAllEle      += 0.02f;                               break;
        }
    }

    // ── HP / MP ───────────────────────────────────────────────
    private void ApplyHPMilestone(int rank)
    {
        switch (rank)
        {
            case 10:  bonusMaxHP   += 100f; bonusMaxMP  += 50f;                      break;
            case 20:  bonusRegenHP += 2f;                                            break;
            case 30:  bonusMaxHP   += 200f;                                          break;
            case 40:  bonusRegenMP += 1f;                                            break;
            case 50:  bonusMaxMP   += 200f;                                          break;
            case 60:  bonusMaxHP   += 300f;                                          break;
            case 70:  bonusRegenHP += 2f;   bonusRegenMP += 1f;                      break;
            case 80:  bonusMaxMP   += 300f;                                          break;
            case 90:  bonusMaxHP   += 500f; bonusMaxMP   += 200f;                    break;
            case 100: bonusMaxHP   += 1000f; bonusMaxMP  += 500f;
                      bonusRegenHP += 2f;    bonusRegenMP += 1f;                     break;
        }
    }

    // =========================================================
    // RECALCUL COMPLET DES BONUS — utilisé par Respec & chargement save
    // =========================================================

    /// <summary>
    /// Recalcule tous les bonus depuis les rangs actuels.
    /// Appelé après un chargement de save pour restaurer l'état.
    /// </summary>
    public void RecalculateAllBonuses()
    {
        ResetBonuses();

        for (int r = 10; r <= rankAttack;    r += 10) ApplyAttackMilestone(r);
        for (int r = 10; r <= rankDefense;   r += 10) ApplyDefenseMilestone(r);
        for (int r = 10; r <= rankElemental; r += 10) ApplyElementalMilestone(r);
        for (int r = 10; r <= rankHP;        r += 10) ApplyHPMilestone(r);
    }

    // =========================================================
    // UTILITAIRES
    // =========================================================

    public int GetRank(StatCategory stat)
    {
        switch (stat)
        {
            case StatCategory.Attack:    return rankAttack;
            case StatCategory.Defense:   return rankDefense;
            case StatCategory.Elemental: return rankElemental;
            case StatCategory.HP:        return rankHP;
            default: return 0;
        }
    }

    private void SetRank(StatCategory stat, int value)
    {
        switch (stat)
        {
            case StatCategory.Attack:    rankAttack    = value; break;
            case StatCategory.Defense:   rankDefense   = value; break;
            case StatCategory.Elemental: rankElemental = value; break;
            case StatCategory.HP:        rankHP        = value; break;
        }
    }

    private void ResetBonuses()
    {
        bonusAttackFlat       = 0f;
        bonusPrecision        = 0f;
        bonusCritChance       = 0f;
        bonusCritMultiplier   = 0f;
        bonusDefenseFlat      = 0f;
        bonusDodge            = 0f;
        bonusCritDmgReduction = 0f;
        bonusResistAllDef     = 0f;
        bonusElementalPoints  = 0f;
        bonusCooldownReduction= 0f;
        bonusResistAllEle     = 0f;
        bonusMaxHP            = 0f;
        bonusMaxMP            = 0f;
        bonusRegenHP          = 0f;
        bonusRegenMP          = 0f;
    }

    // =========================================================
    // ACCESSEURS UI
    // =========================================================

    /// <summary>Points nécessaires pour le prochain rang d'une stat.</summary>
    public int GetNextRankCost(StatCategory stat) => GetCostForNextRank(GetRank(stat));

    /// <summary>True si le joueur peut investir dans cette stat.</summary>
    public bool CanInvest(StatCategory stat)
        => GetRank(stat) < 100 && availablePoints >= GetNextRankCost(stat);

    /// <summary>Résistance élémentaire all totale depuis les StatPoints (Défense + Élémentaire).</summary>
    public float TotalResistAllFromPoints => bonusResistAllDef + bonusResistAllEle;
}

// =========================================================
// ENUM
// =========================================================

public enum StatCategory { Attack, Defense, Elemental, HP }
