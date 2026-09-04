#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

// =============================================================
// CONDITIONDATAEDITOR.CS
// Path : Assets/_Game/Scripts/Progression/Conditions/Editor/ConditionDataEditor.cs
//
// Custom Inspector pour ConditionData.
// Affiche le mode, les entries avec leur checker inline [SerializeReference],
// et les récompenses.
// =============================================================

[CustomEditor(typeof(ConditionData))]
public class ConditionDataEditor : Editor
{
    private ConditionData _data;
    private List<bool>    _foldouts = new List<bool>();

    private static List<Type> _checkerTypes;
    private static string[]   _checkerLabels;

    private void OnEnable()
    {
        _data = (ConditionData)target;
        BuildCheckerList();
    }

    // Scanne tous les types concrets de ConditionCheckerBase
    private static void BuildCheckerList()
    {
        if (_checkerTypes != null) return;

        _checkerTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(ConditionCheckerBase)))
            .OrderBy(t => t.Name)
            .ToList();

        _checkerLabels = _checkerTypes.Select(PrettyName).ToArray();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // ── Identifiant ───────────────────────────────────────
        EditorGUILayout.Space(4);
        DrawSectionLabel("IDENTIFIANT");
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("conditionID"),
            new GUIContent("Condition ID"));

        // ── Mode ──────────────────────────────────────────────
        EditorGUILayout.Space(8);
        DrawSectionLabel("MODE D'ÉVALUATION");
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("mode"),
            new GUIContent("Mode"));

        // ── Sous-conditions ───────────────────────────────────
        EditorGUILayout.Space(8);
        DrawSectionLabel("SOUS-CONDITIONS  (toutes doivent être remplies)");

        var condProp = serializedObject.FindProperty("conditions");
        while (_foldouts.Count < condProp.arraySize) _foldouts.Add(true);

        for (int i = 0; i < condProp.arraySize; i++)
        {
            var entryProp   = condProp.GetArrayElementAtIndex(i);
            var checkerProp = entryProp.FindPropertyRelative("checker");

            Type  currentType  = checkerProp.managedReferenceValue?.GetType();
            Color accent       = GetAccentColor(currentType);
            string headerLabel = currentType != null ? PrettyName(currentType) : "— aucun checker —";
            int    count       = entryProp.FindPropertyRelative("countRequired").intValue;
            int    lvMin       = entryProp.FindPropertyRelative("playerLevelMin").intValue;
            int    lvMax       = entryProp.FindPropertyRelative("playerLevelMax").intValue;
            int    maxW        = entryProp.FindPropertyRelative("maxWinners").intValue;

            // Résumé compact pour le header
            string summary = $"×{count}";
            if (lvMin > 0 || lvMax > 0)
                summary += lvMax > 0 ? $"  lv{lvMin}-{lvMax}" : $"  lv{lvMin}+";
            if (maxW > 0)
                summary += $"  top{maxW}";

            EditorGUILayout.BeginVertical(GUI.skin.box);

            // ── Header foldout ────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            _foldouts[i] = EditorGUILayout.Foldout(
                _foldouts[i],
                $"  [{i}]  {headerLabel}  —  {summary}",
                true,
                MakeFoldoutStyle(accent));

            if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
            {
                condProp.DeleteArrayElementAtIndex(i);
                _foldouts.RemoveAt(i);
                serializedObject.ApplyModifiedProperties();
                return;
            }
            EditorGUILayout.EndHorizontal();

            if (_foldouts[i])
            {
                EditorGUI.indentLevel++;

                // ── Sélecteur de checker ──────────────────────
                DrawSubLabel("CHECKER");
                DrawCheckerSelector(checkerProp, accent);

                // ── Champs du checker ─────────────────────────
                if (checkerProp.managedReferenceValue != null)
                {
                    EditorGUILayout.Space(2);
                    DrawSubLabel("PARAMÈTRES");
                    DrawCheckerFields(checkerProp);
                }

                // ── Filtres globaux + compteur ────────────────
                EditorGUILayout.Space(4);
                DrawSubLabel("COMPTEUR & FILTRES GLOBAUX");

                EditorGUILayout.PropertyField(
                    entryProp.FindPropertyRelative("countRequired"),
                    new GUIContent("Nombre requis"));

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(
                    entryProp.FindPropertyRelative("playerLevelMin"),
                    new GUIContent("Niveau min (0=any)"));
                EditorGUILayout.PropertyField(
                    entryProp.FindPropertyRelative("playerLevelMax"),
                    new GUIContent("Niveau max (0=any)"));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(
                    entryProp.FindPropertyRelative("maxWinners"),
                    new GUIContent("Max gagnants (0=illimité)"));

                // ── Scope ─────────────────────────────────────
                EditorGUILayout.Space(2);
                DrawSubLabel("SCOPE");
                EditorGUILayout.PropertyField(
                    entryProp.FindPropertyRelative("scope"),
                    new GUIContent("Scope"));

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }

        // ── Bouton Ajouter ────────────────────────────────────
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("＋  Ajouter une sous-condition", GUILayout.Width(220), GUILayout.Height(24)))
        {
            condProp.InsertArrayElementAtIndex(condProp.arraySize);
            var newEntry = condProp.GetArrayElementAtIndex(condProp.arraySize - 1);
            newEntry.FindPropertyRelative("checker").managedReferenceValue = null;
            newEntry.FindPropertyRelative("countRequired").intValue        = 1;
            newEntry.FindPropertyRelative("playerLevelMin").intValue       = 0;
            newEntry.FindPropertyRelative("playerLevelMax").intValue       = 0;
            newEntry.FindPropertyRelative("maxWinners").intValue           = 0;
            _foldouts.Add(true);
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // ── Récompenses ───────────────────────────────────────
        EditorGUILayout.Space(12);
        DrawSectionLabel("RÉCOMPENSES");
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("rewards"),
            new GUIContent("Rewards"), true);

        // ── Mail ──────────────────────────────────────────────
        EditorGUILayout.Space(12);
        DrawSectionLabel("MAIL");
        EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"), new GUIContent("Nom (sujet du mail)"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("description"), new GUIContent("Description (corps du mail)"));

        serializedObject.ApplyModifiedProperties();
    }

    // =========================================================
    // SÉLECTEUR DE CHECKER
    // =========================================================

    private void DrawCheckerSelector(SerializedProperty checkerProp, Color accent)
    {
        Type currentType = checkerProp.managedReferenceValue?.GetType();
        int  currentIdx  = currentType != null ? _checkerTypes.IndexOf(currentType) : -1;

        string[] options = new[] { "— aucun —" }.Concat(_checkerLabels).ToArray();
        int displayIdx   = currentIdx + 1;

        EditorGUILayout.BeginHorizontal();
        var labelStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = accent } };
        EditorGUILayout.LabelField("Checker", labelStyle, GUILayout.Width(70));
        int newIdx = EditorGUILayout.Popup(displayIdx, options);
        EditorGUILayout.EndHorizontal();

        if (newIdx == displayIdx) return;

        checkerProp.managedReferenceValue = newIdx == 0
            ? null
            : Activator.CreateInstance(_checkerTypes[newIdx - 1]);
    }

    // =========================================================
    // CHAMPS DU CHECKER
    // =========================================================

    // Champs propres à chaque mode de StatChecker — masqués si mode différent
    private static readonly HashSet<string> _statFinalStatFields = new HashSet<string> { "finalStat", "minValue", "maxValue" };
    private static readonly HashSet<string> _statStatPointFields = new HashSet<string> { "statCategory", "minRank", "maxRank", "exactMilestone" };
    private static readonly HashSet<string> _statAffinityFields  = new HashSet<string> { "affinityElement", "minAffinity", "affinityRankMin", "mustBeDominant" };

    private void DrawCheckerFields(SerializedProperty checkerProp)
    {
        bool isStatChecker = checkerProp.managedReferenceValue is StatChecker;
        int  statMode      = -1;

        if (isStatChecker)
        {
            var modeProp = checkerProp.FindPropertyRelative("mode");
            if (modeProp != null) statMode = modeProp.intValue;
        }

        var copy = checkerProp.Copy();
        var end  = checkerProp.GetEndProperty();

        if (!copy.NextVisible(true)) return;

        while (!SerializedProperty.EqualContents(copy, end))
        {
            if (copy.depth <= checkerProp.depth) break;

            // ── Filtrage contextuel pour StatChecker ──────────
            if (isStatChecker && statMode >= 0)
            {
                string fn = copy.name;
                bool skip = false;
                if (_statFinalStatFields.Contains(fn) && statMode != 0) skip = true; // CheckMode.FinalStat = 0
                if (_statStatPointFields.Contains(fn) && statMode != 1) skip = true; // CheckMode.StatPoint = 1
                if (_statAffinityFields.Contains(fn)  && statMode != 2) skip = true; // CheckMode.Affinity  = 2

                if (skip)
                {
                    if (!copy.NextVisible(false)) break;
                    continue;
                }
            }

            EditorGUILayout.PropertyField(copy, true);
            if (!copy.NextVisible(false)) break;
        }
    }

    // =========================================================
    // HELPERS UI
    // =========================================================

    private void DrawSectionLabel(string text)
    {
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11,
            normal   = { textColor = new Color(0.9f, 0.8f, 0.5f) }
        };
        EditorGUILayout.LabelField(text, style);
        Rect r = GUILayoutUtility.GetLastRect();
        r.y += r.height - 1; r.height = 1;
        EditorGUI.DrawRect(r, new Color(0.9f, 0.8f, 0.5f, 0.4f));
        EditorGUILayout.Space(2);
    }

    private void DrawSubLabel(string text)
    {
        EditorGUILayout.Space(3);
        var style = new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.5f, 0.8f, 0.6f) }
        };
        EditorGUILayout.LabelField(text, style);
    }

    private GUIStyle MakeFoldoutStyle(Color accent)
    {
        var s = new GUIStyle(EditorStyles.foldout);
        s.normal.textColor    = accent;
        s.onNormal.textColor  = accent;
        s.focused.textColor   = accent;
        s.onFocused.textColor = accent;
        return s;
    }

    private static string PrettyName(Type t)
    {
        string name = t.Name
            .Replace("Checker", "")
            .Replace("Validator", "");
        return System.Text.RegularExpressions.Regex.Replace(name, "(?<!^)([A-Z])", " $1").Trim();
    }

    private static Color GetAccentColor(Type t)
    {
        if (t == null) return Color.gray;

        // ── Checkers actuels (typeof safe) ────────────────────
        if (t == typeof(KillChecker))        return new Color(1.0f, 0.40f, 0.40f);
        if (t == typeof(DamageChecker))      return new Color(1.0f, 0.60f, 0.25f);
        if (t == typeof(SkillCastChecker))   return new Color(0.75f, 0.50f, 1.0f);
        if (t == typeof(DebuffChecker))      return new Color(0.60f, 0.85f, 0.60f); // ex-DebuffReceivedChecker
        if (t == typeof(NpcInteractChecker)) return new Color(1.0f, 0.85f, 0.40f);
        if (t == typeof(StatChecker))        return new Color(0.55f, 0.70f, 1.0f);  // fusionne PlayerLevelChecker / StatThresholdChecker / StatPointChecker / AffinityChecker
        if (t == typeof(ZoneChecker))        return new Color(0.40f, 1.0f, 0.55f);
        if (t == typeof(SocialChecker))      return new Color(0.85f, 0.85f, 0.85f);
        if (t == typeof(TimeChecker))        return new Color(0.40f, 0.95f, 0.95f); // couvre aussi FirstConnectionChecker (action=Login)
        if (t == typeof(QuestChecker))       return new Color(1.0f, 0.70f, 0.50f);
        if (t == typeof(DeathChecker))       return new Color(0.80f, 0.30f, 0.30f);

        // ── Checkers non encore implémentés — par nom de string
        // (évite les erreurs CS0246 si le type n'existe pas encore)
        switch (t.Name)
        {
            case "ItemChecker":     return new Color(1.0f, 0.90f, 0.30f);
            case "ActivityChecker": return new Color(0.95f, 0.70f, 0.40f);
            case "PetChecker":      return new Color(0.60f, 0.95f, 0.70f);
        }

        return Color.white; // tout nouveau checker ajouté plus tard
    }
}
#endif
