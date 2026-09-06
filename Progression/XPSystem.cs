using UnityEngine;

// =============================================================
// XPSYSTEM.CS — Gestion XP combat
// Path : Assets/Scripts/Systems/XPSystem.cs
// AetherTree GDD v30 — Section 15
//
// S'abonne à GameEventBus.OnMobKilled — plus d'appel direct
// depuis Mob.Die() ou LootManager.
// Les données runtime (level, xpCombat) vivent dans Player.cs.
// CharacterData est un SO template immuable — on n'y écrit JAMAIS.
//
// TODO Phase 8 : bonus groupe §15.2
//   2 joueurs = +20% | 3 = +35% | 4 = +50% | 5+ = +60% (plafond)
//   isInParty + partySize disponibles via SkillUsedEvent — à brancher ici
// =============================================================

public class XPSystem : MonoBehaviour
{
    public static XPSystem Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        GameEventBus.OnMobKilled += HandleMobKilled;
    }

    private void OnDisable()
    {
        GameEventBus.OnMobKilled -= HandleMobKilled;
    }

    /// <summary>
    /// Appelé par GameEventBus.Reset() après un changement de map.
    /// Réabonne XPSystem après le null-reset de tous les events.
    /// </summary>
    public void Resubscribe()
    {
        GameEventBus.OnMobKilled -= HandleMobKilled; // évite le double abonnement
        GameEventBus.OnMobKilled += HandleMobKilled;
    }

    // =========================================================
    // HANDLER
    // =========================================================

    private void HandleMobKilled(MobKilledEvent e)
    {
        if (e.mob == null) return;

        if (e.eligiblePlayers != null && e.eligiblePlayers.Count > 0)
        {
            // XP depuis LootTable — source de vérité centralisée
            int xp = e.mob.lootTable?.xpReward ?? 0;
            if (xp > 0)
                foreach (Player p in e.eligiblePlayers)
                    GiveCombatXP(p, xp);
        }

        GiveSpiritXP(e);
    }

    /// <summary>XP Esprit — GDD §5.8 : chaque mob tué dans la plage ±15 niveaux du joueur
    /// accorde 1 XP à l'Esprit actif, à condition que le joueur ait contribué au kill (≥1 hit
    /// — seuil bien plus bas que l'éligibilité XP joueur/loot à 10%, voir
    /// MobKilledEvent.contributingPlayers). Boosté par StatModifierType.SpiritXpBonus
    /// (talisman Spirit_XP), même schéma que XPBonus côté XP joueur.</summary>
    private void GiveSpiritXP(MobKilledEvent e)
    {
        if (e.contributingPlayers == null || e.contributingPlayers.Count == 0) return;

        const int MAX_LEVEL_GAP = 15;

        foreach (Player p in e.contributingPlayers)
        {
            if (p == null) continue;
            if (Mathf.Abs(p.level - e.mobLevel) > MAX_LEVEL_GAP) continue;
            if (p.equippedSpiritInstances == null || p.equippedSpiritInstances.Count == 0) continue;

            SpiritInstance spirit = p.equippedSpiritInstances[0];
            int boosted = Mathf.Max(1, Mathf.RoundToInt(1 * (1f + p.SpiritXpBonusPercent)));
            bool leveledUp = spirit.AddXP(boosted);
            if (leveledUp)
                p.RequestRecalculate();
        }
    }

    // =========================================================
    // DISTRIBUTION XP
    // =========================================================

    private void GiveCombatXP(Player target, int amount)
    {
        if (target == null || amount <= 0) return;

        // Bonus talisman (XPBonus, ex: +20%) — appliqué ici pour que le texte flottant
        // affiche déjà le montant boosté, pas le montant brut de la LootTable.
        int boosted = Mathf.RoundToInt(amount * (1f + target.XPBonusPercent));
        target.AddCombatXP(boosted);

        FloatingText.Spawn(
            $"+{boosted} XP",
            target.transform.position + UnityEngine.Vector3.up * 2f,
            UnityEngine.Color.cyan);
    }

    // =========================================================
    // UTILITAIRES
    // =========================================================

    /// <summary>
    /// Courbe XP fallback — XP nécessaire pour passer au niveau suivant.
    /// Utilisé si CharacterData.xpThresholds ne couvre pas le niveau.
    /// ⚠ Cette formule (100 × level^1.5) est une approximation dev —
    /// elle ne correspond pas exactement à la courbe §15.3 du GDD.
    /// Référence GDD §15.3 : lv1-10 ~220/niv | lv91-100 ~7 826 000/niv.
    /// À remplacer par une table de valeurs précises une fois calibrée.
    /// </summary>
    public static int CalculateXPForLevel(int level)
    {
        return UnityEngine.Mathf.RoundToInt(100 * UnityEngine.Mathf.Pow(level, 1.5f));
    }
}