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

            SpiritInstance spirit  = p.equippedSpiritInstances[0];
            const int      baseXp  = 1;
            int            boosted = Mathf.Max(1, RollFractionalAmount(baseXp * (1f + p.SpiritXpBonusPercent)));
            int            bonus   = boosted - baseXp;
            bool leveledUp = spirit.AddXP(boosted);
            if (leveledUp)
                p.RequestRecalculate();

            Debug.Log($"[XP] {p.entityName} — Esprit {spirit.data?.name} : {baseXp} XP brut + {bonus} bonus ({p.SpiritXpBonusPercent:P0}) = {boosted} total.");
        }
    }

    // =========================================================
    // DISTRIBUTION XP
    // =========================================================

    /// <summary>Arrondi probabiliste — nécessaire pour GiveSpiritXP où la base (1) est trop
    /// petite pour qu'un bonus % survive à un Mathf.RoundToInt classique (1 × 1.25 = 1.25,
    /// arrondit TOUJOURS vers 1, le bonus disparaît intégralement à chaque kill, jamais
    /// accumulé). Ex: 1.25 → 75% de chance de 1, 25% de chance de 2 — moyenne exacte sur
    /// beaucoup de tirages, au lieu d'un arrondi qui perd la fraction à chaque fois.</summary>
    private int RollFractionalAmount(float exact)
    {
        int   whole = Mathf.FloorToInt(exact);
        float frac  = exact - whole;
        return whole + (UnityEngine.Random.value < frac ? 1 : 0);
    }

    private void GiveCombatXP(Player target, int amount)
    {
        if (target == null || amount <= 0) return;

        // Bonus talisman (XPBonus, ex: +20%) — appliqué ici pour que le texte flottant
        // affiche déjà le montant boosté, pas le montant brut de la LootTable.
        int boosted = Mathf.RoundToInt(amount * (1f + target.XPBonusPercent));
        int bonus   = boosted - amount;
        target.AddCombatXP(boosted);

        string label = bonus > 0 ? $"+{boosted} XP (+{bonus} XP bonus)" : $"+{boosted} XP";
        FloatingText.Spawn(
            label,
            target.transform.position + UnityEngine.Vector3.up * 2f,
            UnityEngine.Color.cyan);

        Debug.Log($"[XP] {target.entityName} : {amount} XP brut + {bonus} bonus ({target.XPBonusPercent:P0}) = {boosted} total.");
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