using UnityEngine;
using System.Collections.Generic;

// =============================================================
// PERMANENTSKILLDATA — ScriptableObject de skill permanent
// Path : Assets/Scripts/Data/Skills/PermanentSkillData.cs
// AetherTree GDD v30 — §16
//
// Un skill permanent n'est pas exécuté — il modifie les stats
// du joueur de façon passive et permanente dès qu'il est débloqué.
//
// Sources de déblocage :
//   - Achat chez un marchand (ShopUI)
//   - Récompense UnlockManager (MailboxSystem)
//   - Récompense de quête
//
// Les bonuses sont appliqués dans PlayerStats.RecalculateStats()
// via Player.unlockedSkills (cast en PermanentSkillData).
//
// Assets > Create > AetherTree > Skills > PermanentSkillData
// =============================================================

[CreateAssetMenu(fileName = "perm_", menuName = "AetherTree/Skills/PermanentSkillData")]
public class PermanentSkillData : ScriptableObject
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"perm_\"\n" +
             "(ex: \"perm_vitalite_1\"). Ne JAMAIS afficher au joueur — voir skillName pour l'affichage.")]
    public string permanentID;
    [Tooltip("Nom affiché au joueur (fr/en). Ne jamais utiliser dans un log/comparaison —\n" +
             "utiliser this.name (nom d'asset Unity, déjà la clé stable) pour ça.")]
    public LocalizedText skillName  = new LocalizedText();
    public LocalizedText description = new LocalizedText();
    public Sprite icon;

    // ── Catégorie visuelle (pour le tooltip / SkillLibrary) ───
    [Header("Catégorie")]
    [Tooltip("Catégorie affichée dans la bibliothèque et le tooltip.\n" +
             "Ex: Vitalité, Attaque, Défense, Élémentaire, Mobilité...")]
    public string category = "Vitalité";

    // ── Bonuses permanents ────────────────────────────────────
    [Header("① Bonus stats permanents")]
    [Tooltip("Bonuses appliqués définitivement aux stats du joueur.\n" +
             "Même système que les équipements — StatType + valeur.\n\n" +
             "Ex: BonusHP 500 | MeleeDefense 20 | CritChance 0.05")]
    public List<StatBonus> bonuses = new List<StatBonus>();

    [Header("② Résistances aux debuffs")]
    [Tooltip("Chances permanentes de résister à un debuff spécifique.\n" +
             "Ex: Stun 0.10 = 10% de chance de résister à l'étourdissement.")]
    public List<DebuffResistanceEntry> debuffResistances = new List<DebuffResistanceEntry>();

    [Header("③ Effets On-Hit reçus (déclenchés quand on reçoit un coup)")]
    [Tooltip("Effets déclenchés quand le joueur reçoit un coup.\n" +
             "Ex: Thorns 5 dmg | CounterPoison 8% | HealOnHit 1% MaxHP")]
    public List<OnHitReceivedEffectEntry> onHitReceivedEffects = new List<OnHitReceivedEffectEntry>();

    [Header("④ Effets On-Hit infligés (déclenchés quand on touche une cible)")]
    [Tooltip("Effets déclenchés quand le joueur touche une cible.\n" +
             "Ex: LifestealOnHit 10% | ManaOnHit 5")]
    public List<OnHitDealtEffectEntry> onHitDealtEffects = new List<OnHitDealtEffectEntry>();

    // ── Helpers ───────────────────────────────────────────────

    /// <summary>
    /// Résumé lisible des bonuses pour le tooltip.
    /// Ex: "+500 HP max  |  +20 Déf. mêlée"
    /// </summary>
    public string GetBonusSummary()
    {
        if (bonuses == null || bonuses.Count == 0) return "Aucun bonus";

        var parts = new System.Text.StringBuilder();
        foreach (StatBonus b in bonuses)
        {
            if (parts.Length > 0) parts.Append("  |  ");

            bool isRatio = b.statType == StatType.CritChance
                        || b.statType == StatType.CritMultiplier
                        || b.statType.ToString().StartsWith("Resist")
                        || b.statType.ToString().StartsWith("Element");

            string valStr = isRatio
                ? $"+{b.value * 100f:F0}%"
                : $"+{b.value:F0}";

            parts.Append($"{valStr} {StatLabel(b.statType)}");
        }
        return parts.ToString();
    }

    private string StatLabel(StatType t) => t switch
    {
        StatType.BonusHP         => "HP max",
        StatType.BonusMana       => "Mana max",
        StatType.BonusRegenHP    => "Regen HP/s",
        StatType.BonusRegenMana  => "Regen Mana/s",
        StatType.MeleeDefense    => "Déf. mêlée",
        StatType.RangedDefense   => "Déf. distance",
        StatType.MagicDefense    => "Déf. magie",
        StatType.BonusAttack     => "Attaque",
        StatType.CritChance      => "Chance critique",
        StatType.CritMultiplier  => "Dégâts crit.",
        StatType.Dodge           => "Esquive",
        StatType.Precision       => "Précision",
        StatType.MoveSpeed       => "Vitesse",
        StatType.ResistFire      => "Résist. Feu",
        StatType.ResistWater     => "Résist. Eau",
        StatType.ResistEarth     => "Résist. Terre",
        StatType.ResistNature    => "Résist. Nature",
        StatType.ResistLightning => "Résist. Foudre",
        StatType.ResistDarkness  => "Résist. Ténèbres",
        StatType.ResistLight     => "Résist. Lumière",
        StatType.ResistAll       => "Résist. Tout",
        StatType.PointsFire      => "Pts Feu",
        StatType.PointsWater     => "Pts Eau",
        StatType.PointsEarth     => "Pts Terre",
        StatType.PointsNature    => "Pts Nature",
        StatType.PointsLightning => "Pts Foudre",
        StatType.PointsDarkness  => "Pts Ténèbres",
        StatType.PointsLight     => "Pts Lumière",
        StatType.PointsAll       => "Pts Élém.",
        _                        => t.ToString()
    };

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(permanentID))
            permanentID = name;
    }
#endif
}