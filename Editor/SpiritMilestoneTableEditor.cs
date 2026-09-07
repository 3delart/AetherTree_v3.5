#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;

// =============================================================
// SpiritMilestoneTableEditor.cs — aperçu du gain cumulé par niveau
// Path : Assets/Scripts/Editor/SpiritMilestoneTableEditor.cs
// AetherTree GDD v3.6 — §5.8 (2026-09-07, demande Florian)
//
// `elementalPointGains`/`neutralStatGains` sont 2 listes éditables SÉPARÉES (level → valeurs) —
// pas une liste unique à 5 champs, trop confus (un esprit élémentaire n'utilise jamais les 4
// champs Neutre et vice-versa). Ce custom Editor ajoute juste un aperçu repliable en LECTURE
// SEULE sous l'Inspector par défaut, montrant le total CUMULÉ à chaque niveau 1-100 (somme de
// toutes les lignes ≤ ce niveau), recalculé en direct, PLUS les paliers débloqués À ce niveau
// précis (milestones/neutralMilestones — pas cumulé, juste ce qui apparaît à ce niveau) et une
// section séparée listant tous les procs par élément (elementProcs, pas dans le tableau — trop
// large pour 8 éléments × plusieurs procs). Le bouton "Pré-remplir" génère la table finale
// 2026-09-07 (chiffres ronds, un gain à CHAQUE niveau pour le Neutre, RAMPÉ par décennie —
// sinon lvl99 ne donne que +1 attaque, "nul à chier" dixit Florian) que Florian peut ensuite
// éditer ligne par ligne (ex: changer lvl 43 à la main).
// =============================================================
[CustomEditor(typeof(SpiritMilestoneTable))]
public class SpiritMilestoneTableEditor : Editor
{
    private bool _showPreview = true;
    private bool _showProcs   = true;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var table = (SpiritMilestoneTable)target;

        EditorGUILayout.Space(10);
        if (GUILayout.Button("Pré-remplir (table finale 2026-09-07)"))
        {
            Undo.RecordObject(table, "Pré-remplir gains par niveau");
            SeedStandardCurve(table);
            EditorUtility.SetDirty(table);
        }

        EditorGUILayout.Space(6);
        _showPreview = EditorGUILayout.Foldout(_showPreview, "Aperçu gain par niveau (1-100)", true);
        if (_showPreview)
        {
            EditorGUILayout.HelpBox(
                "Points/stats : total CUMULÉ à CE niveau. Paliers : ce qui se débloque À ce\n" +
                "niveau précis (pas cumulé — voir la colonne pour le détail).", MessageType.Info);

            DrawHeaderRow();
            for (int lvl = 1; lvl <= 100; lvl++)
                DrawLevelRow(table, lvl);
        }

        EditorGUILayout.Space(10);
        _showProcs = EditorGUILayout.Foldout(_showProcs, "Procs par élément (elementProcs)", true);
        if (_showProcs)
            DrawProcs(table);
    }

    /// <summary>Génère les gains par niveau — table finale 2026-09-07 (Florian, rev.3 — cible
    ///   exacte lvl100 : 200 attaque / 1500 HP / 6% crit chance / 5% résist ALL) :
    ///   Points élémentaires : 1 ligne tous les 10 niveaux (10,20,...,100), valeur = le niveau
    ///     lui-même → total 550 à lvl100.
    ///   Esprit Neutre : un gain à CHAQUE niveau, rampé par PAIRE de décennies (pairIndex 1-5,
    ///     regroupe 2 décennies pour retomber sur des totaux ronds exacts) :
    ///     impair = +(1+pairIndex) Attaque → 2,2,3,3,4,4,5,5,6,6 par décennie → 200 à lvl100.
    ///     pair   = +(15+5×pairIndex) HP → 20,20,25,25,30,30,35,35,40,40 → 1500 à lvl100.
    ///     Résist ALL : +1% à Lv25/50/75, +2% à Lv100 → 5% total.
    ///     Crit Chance : +1% à Lv40, +2% à Lv70, +3% à Lv100 → 6% total (inchangé depuis rev.2).
    ///   Empilés sur la même ligne si coïncidence (ex: Lv100 = HP pair + résist + crit).
    ///   Paliers Neutre (neutralMilestones) : même cadence 10/20/.../100 que les élémentaires —
    ///     rotation CritMultiplier → FinalDamageBonus → FinalDamageReduction → CooldownReduction
    ///     → repeat, +2% à chaque palier (1 seule stat/palier, valeur doublée plutôt que 2
    ///     stats à moitié prix — demande Florian).
    ///   Écrase les listes existantes, point de départ à retoucher à la main.
    /// </summary>
    private static void SeedStandardCurve(SpiritMilestoneTable table)
    {
        table.elementalPointGains.Clear();
        for (int lvl = 10; lvl <= 100; lvl += 10)
            table.elementalPointGains.Add(new SpiritElementalPointGain { level = lvl, elementalPoints = lvl });

        table.neutralStatGains.Clear();
        var byLevel = new Dictionary<int, SpiritNeutralStatGain>();
        SpiritNeutralStatGain GetOrCreate(int lvl)
        {
            if (!byLevel.TryGetValue(lvl, out var g))
            {
                g = new SpiritNeutralStatGain { level = lvl };
                byLevel[lvl] = g;
            }
            return g;
        }

        for (int lvl = 1; lvl <= 100; lvl++)
        {
            int palier = (lvl - 1) / 10 + 1;
            int pairIndex = (palier - 1) / 2 + 1; // 1..5, regroupe 2 décennies
            if (lvl % 2 == 1) GetOrCreate(lvl).neutralAttack = 1 + pairIndex;       // total 200
            else              GetOrCreate(lvl).neutralHP     = 15 + 5 * pairIndex; // total 1500
        }
        GetOrCreate(25).neutralResistAll  = 0.01f;
        GetOrCreate(50).neutralResistAll  = 0.01f;
        GetOrCreate(75).neutralResistAll  = 0.01f;
        GetOrCreate(100).neutralResistAll = 0.02f; // total 5%

        GetOrCreate(40).neutralCritChance  = 0.01f;
        GetOrCreate(70).neutralCritChance  = 0.02f;
        GetOrCreate(100).neutralCritChance = 0.03f; // total 6%

        var sortedLevels = new List<int>(byLevel.Keys);
        sortedLevels.Sort();
        foreach (var lvl in sortedLevels)
            table.neutralStatGains.Add(byLevel[lvl]);

        // Paliers Neutre (neutralMilestones, StatBonus — pipeline prêt pour les 4 : Crit
        // Multiplier / FinalDamageBonus / FinalDamageReduction existaient déjà, CooldownReduction
        // ajouté ce même jour) — même cadence que les élémentaires (10/20/.../100). 1 SEULE stat
        // par palier à valeur DOUBLÉE plutôt que 2 stats à moitié prix (demande Florian — "1
        // stat plus élevée est mieux que deux stats basses") : rotation CritMultiplier →
        // FinalDamageBonus → FinalDamageReduction → CooldownReduction → repeat, +2% à chaque
        // palier. Totaux à lvl100 : CritMult 6% (×3), FinalDamageBonus 6% (×3),
        // FinalDamageReduction 4% (×2), CooldownReduction 4% (×2).
        table.neutralMilestones.Clear();
        var rotation = new[]
        {
            StatType.CritMultiplier, StatType.FinalDamageBonus,
            StatType.FinalDamageReduction, StatType.CooldownReduction,
        };
        int rotationIndex = 0;
        for (int lvl = 10; lvl <= 100; lvl += 10)
        {
            var statType = rotation[rotationIndex % rotation.Length];
            rotationIndex++;

            var bonus = new StatBonus { statType = statType, value = 0.02f };
            if (statType == StatType.FinalDamageBonus || statType == StatType.FinalDamageReduction)
                bonus.mode = ModifierType.Percent;

            table.neutralMilestones.Add(new SpiritMilestone
            {
                level = lvl,
                bonuses = new List<StatBonus> { bonus },
            });
        }
    }

    private static void DrawHeaderRow()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Lvl",        GUILayout.Width(28));
        EditorGUILayout.LabelField("Élém. Pts",  GUILayout.Width(60));
        EditorGUILayout.LabelField("Atk", GUILayout.Width(45));
        EditorGUILayout.LabelField("HP",  GUILayout.Width(45));
        EditorGUILayout.LabelField("Crit",   GUILayout.Width(55));
        EditorGUILayout.LabelField("Rés", GUILayout.Width(55));
        EditorGUILayout.LabelField("Palier Élém.", GUILayout.Width(140));
        EditorGUILayout.LabelField("Palier Neutre", GUILayout.Width(160));
        EditorGUILayout.EndHorizontal();
    }

    private static void DrawLevelRow(SpiritMilestoneTable table, int lvl)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(lvl.ToString(), GUILayout.Width(28));
        EditorGUILayout.LabelField(Mathf.RoundToInt(table.GetElementalPointsAt(lvl)).ToString(), GUILayout.Width(60));
        EditorGUILayout.LabelField(table.GetNeutralAttackAt(lvl).ToString("F0"), GUILayout.Width(45));
        EditorGUILayout.LabelField(table.GetNeutralHPAt(lvl).ToString("F0"), GUILayout.Width(45));
        EditorGUILayout.LabelField((table.GetNeutralCritChanceAt(lvl) * 100f).ToString("F1") + "%", GUILayout.Width(55));
        EditorGUILayout.LabelField((table.GetNeutralResistAllAt(lvl) * 100f).ToString("F1") + "%", GUILayout.Width(55));
        EditorGUILayout.LabelField(FormatElementalMilestone(table, lvl), GUILayout.Width(140));
        EditorGUILayout.LabelField(FormatNeutralMilestone(table, lvl), GUILayout.Width(160));
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>Détail du palier élémentaire débloqué À ce niveau précis (pas cumulé) — vide si
    /// aucun palier à ce niveau.</summary>
    private static string FormatElementalMilestone(SpiritMilestoneTable table, int lvl)
    {
        if (table.milestones == null) return "";
        foreach (var m in table.milestones)
        {
            if (m.level != lvl) continue;
            var sb = new StringBuilder();
            if (m.resistBonusPercent != 0f)          sb.Append($"Rés+{m.resistBonusPercent * 100f:F0}% ");
            if (m.elementalDamageBonusPercent != 0f) sb.Append($"Dmg+{m.elementalDamageBonusPercent * 100f:F0}% ");
            if (m.manaCostReductionPercent != 0f)    sb.Append($"Mana{m.manaCostReductionPercent * 100f:F0}% ");
            if (m.resistPenetrationPercent != 0f)    sb.Append($"Pén{m.resistPenetrationPercent * 100f:F0}%");
            return sb.ToString();
        }
        return "";
    }

    /// <summary>Détail du palier Neutre (StatBonus) débloqué À ce niveau précis — vide si aucun
    /// palier à ce niveau.</summary>
    private static string FormatNeutralMilestone(SpiritMilestoneTable table, int lvl)
    {
        if (table.neutralMilestones == null) return "";
        foreach (var m in table.neutralMilestones)
        {
            if (m.level != lvl) continue;
            if (m.bonuses == null || m.bonuses.Count == 0) return "(vide)";
            var sb = new StringBuilder();
            foreach (var b in m.bonuses)
            {
                string unit = b.mode == ModifierType.Percent ? "%" : "";
                float val = b.mode == ModifierType.Percent ? b.value * 100f : b.value;
                sb.Append($"{b.statType}+{val:0.##}{unit} ");
            }
            return sb.ToString();
        }
        return "";
    }

    private static void DrawProcs(SpiritMilestoneTable table)
    {
        if (table.elementProcs == null || table.elementProcs.Count == 0)
        {
            EditorGUILayout.LabelField("(aucun proc configuré)");
            return;
        }

        foreach (var set in table.elementProcs)
        {
            if (set.procs == null || set.procs.Count == 0) continue;

            EditorGUILayout.LabelField(set.element.ToString(), EditorStyles.boldLabel);
            foreach (var p in set.procs)
            {
                string effectName = p.direction == OnHitProcDirection.Dealt
                    ? (p.dealtEffect != null ? p.dealtEffect.name : "(vide)")
                    : (p.receivedEffect != null ? p.receivedEffect.name : "(vide)");
                EditorGUILayout.LabelField(
                    $"    Lv{p.level}  {p.direction}  {effectName}  ({p.chance * 100f:F0}%)");
            }
        }
    }
}
#endif
