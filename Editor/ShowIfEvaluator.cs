#if UNITY_EDITOR
using UnityEditor;

// =============================================================
// SHOWIFEVALUATOR.CS — Logique de condition [ShowIf] partagée entre ShowIfPropertyDrawer
// (champs scalaires) et ShowIfEditor (TOUS les champs, notamment List<>/array — voir le
// commentaire d'en-tête de ShowIfEditor.cs pour pourquoi les deux chemins existent séparément).
// Path : Assets/Scripts/Editor/ShowIfEvaluator.cs
//
// Extrait de ShowIfPropertyDrawer le 2026-10-04 (était privé là-bas) — même logique exacte,
// juste rendue réutilisable pour ne pas la dupliquer/laisser diverger entre les deux appelants.
// =============================================================
public static class ShowIfEvaluator
{
    public static bool IsVisible(SerializedProperty property, ShowIfAttribute showIf)
    {
        if (!MatchesAny(property, showIf.conditionField, showIf.values)) return false;

        // AndField optionnel — condition ET secondaire (voir ShowIfAttribute.AndField).
        // Absente (null) par défaut → n'affecte aucun des usages existants à un seul champ.
        if (showIf.AndField != null && !MatchesAny(property, showIf.AndField, new[] { showIf.AndValue }))
            return false;

        // ExcludeField optionnel — condition d'exclusion, slot INDÉPENDANT d'AndField (voir
        // ShowIfAttribute.ExcludeField). Absente (null) par défaut → aucun effet sur les
        // usages existants.
        if (showIf.ExcludeField != null && MatchesAny(property, showIf.ExcludeField, new[] { showIf.ExcludeValue }))
            return false;

        return true;
    }

    /// <summary>Résout le champ frère `fieldName` (chemin relatif, robuste dans les listes/objets
    /// imbriqués — ex: "effects.Array.data[0].effectType", contrairement à un simple
    /// Replace(property.name, ...) qui peut matcher au mauvais endroit) et retourne true si sa
    /// valeur correspond à AU MOINS UNE des `values` fournies.</summary>
    public static bool MatchesAny(SerializedProperty property, string fieldName, object[] values)
    {
        int lastDot = property.propertyPath.LastIndexOf('.');
        string siblingPath = lastDot >= 0
            ? property.propertyPath.Substring(0, lastDot + 1) + fieldName
            : fieldName;

        SerializedProperty condition = property.serializedObject.FindProperty(siblingPath);
        if (condition == null) return true; // champ introuvable — n'échoue pas silencieusement en masquant tout

        foreach (var value in values)
        {
            switch (condition.propertyType)
            {
                case SerializedPropertyType.Enum:
                    int index = condition.enumValueIndex;
                    if (index >= 0 && index < condition.enumNames.Length &&
                        condition.enumNames[index] == value.ToString())
                        return true;
                    break;

                case SerializedPropertyType.Boolean:
                    if (value is bool b && condition.boolValue == b)
                        return true;
                    break;
            }
        }
        return false;
    }
}
#endif
