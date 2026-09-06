#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

// =============================================================
// SHOWIFPROPERTYDRAWER.CS — Rendu de [ShowIf], voir Utils/ShowIfAttribute.cs
// Path : Assets/Scripts/Editor/ShowIfPropertyDrawer.cs
//
// Un seul drawer générique pour tout le projet — masque le champ (hauteur 0,
// pas de dessin) si le champ frère "conditionField" ne correspond à aucune
// des valeurs listées dans l'attribut. Supporte enum (comparaison par nom,
// robuste aux réordonnancements) et bool.
//
// Deux points délicats gérés ici :
//   - Header conditionnel : dessiné par CE drawer (attribute.Header) plutôt
//     que via [Header] classique, qui resterait affiché même masqué.
//   - Range/Min : Unity ne garde qu'un seul PropertyDrawer par champ quand
//     plusieurs sont empilés. order=-1 sur ShowIfAttribute force celui-ci à
//     être choisi, donc le slider Range/Min doit être redessiné ICI à la
//     main (détecté via réflexion sur fieldInfo) pour ne pas le perdre.
// =============================================================

[CustomPropertyDrawer(typeof(ShowIfAttribute))]
public class ShowIfPropertyDrawer : PropertyDrawer
{
    private const float HeaderHeight = 18f;
    private const float HeaderSpacing = 2f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!IsVisible(property))
            return -EditorGUIUtility.standardVerticalSpacing; // annule l'espacement auto entre champs

        float height = EditorGUI.GetPropertyHeight(property, label, true);
        if (!string.IsNullOrEmpty(((ShowIfAttribute)attribute).Header))
            height += HeaderHeight + HeaderSpacing;
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (!IsVisible(property)) return;

        var showIf = (ShowIfAttribute)attribute;

        // Copie défensive — EditorGUI.LabelField(rect, string, style) construit son propre
        // GUIContent via un pool interne à Unity (EditorGUIUtility.TempContent) qui peut
        // partager LA MÊME instance que le `label` transmis par Unity pour ce champ. Sans
        // cette copie, dessiner le header (ci-dessous, AVANT le champ) écrase silencieusement
        // le texte du label du champ lui-même par celui du header — bug vécu : "Damage
        // Multiplier" affichait le texte du Header à la place de son propre nom.
        var fieldLabel = new GUIContent(label);
        if (!string.IsNullOrEmpty(showIf.DisplayName))
            fieldLabel.text = showIf.DisplayName;

        if (!string.IsNullOrEmpty(showIf.Header))
        {
            var headerRect = new Rect(position.x, position.y, position.width, HeaderHeight);
            EditorGUI.LabelField(headerRect, showIf.Header, EditorStyles.boldLabel);
            position.y      += HeaderHeight + HeaderSpacing;
            position.height -= HeaderHeight + HeaderSpacing;
        }

        // Redessine Range/Min à la main — leur propre PropertyDrawer ne tourne
        // jamais ici (un seul drawer par champ, voir le commentaire d'en-tête).
        var rangeAttr = fieldInfo.GetCustomAttributes(typeof(RangeAttribute), false);
        if (rangeAttr.Length > 0 && property.propertyType == SerializedPropertyType.Float)
        {
            var range = (RangeAttribute)rangeAttr[0];
            EditorGUI.BeginProperty(position, fieldLabel, property);
            property.floatValue = EditorGUI.Slider(position, fieldLabel, property.floatValue, range.min, range.max);
            EditorGUI.EndProperty();
            return;
        }
        if (rangeAttr.Length > 0 && property.propertyType == SerializedPropertyType.Integer)
        {
            var range = (RangeAttribute)rangeAttr[0];
            EditorGUI.BeginProperty(position, fieldLabel, property);
            property.intValue = EditorGUI.IntSlider(position, fieldLabel, property.intValue, (int)range.min, (int)range.max);
            EditorGUI.EndProperty();
            return;
        }

        var minAttr = fieldInfo.GetCustomAttributes(typeof(MinAttribute), false);
        if (minAttr.Length > 0 && property.propertyType == SerializedPropertyType.Float)
        {
            var min = (MinAttribute)minAttr[0];
            EditorGUI.BeginProperty(position, fieldLabel, property);
            property.floatValue = Mathf.Max(min.min, EditorGUI.FloatField(position, fieldLabel, property.floatValue));
            EditorGUI.EndProperty();
            return;
        }
        if (minAttr.Length > 0 && property.propertyType == SerializedPropertyType.Integer)
        {
            var min = (MinAttribute)minAttr[0];
            EditorGUI.BeginProperty(position, fieldLabel, property);
            property.intValue = Mathf.Max((int)min.min, EditorGUI.IntField(position, fieldLabel, property.intValue));
            EditorGUI.EndProperty();
            return;
        }

        EditorGUI.PropertyField(position, property, fieldLabel, true);
    }

    private bool IsVisible(SerializedProperty property)
    {
        var showIf = (ShowIfAttribute)attribute;

        if (!MatchesAny(property, showIf.conditionField, showIf.values)) return false;

        // AndField optionnel — condition ET secondaire (voir ShowIfAttribute.AndField).
        // Absente (null) par défaut → n'affecte aucun des usages existants à un seul champ.
        if (showIf.AndField != null && !MatchesAny(property, showIf.AndField, new[] { showIf.AndValue }))
            return false;

        return true;
    }

    /// <summary>Résout le champ frère `fieldName` (chemin relatif, robuste dans les listes/objets
    /// imbriqués — ex: "effects.Array.data[0].effectType", contrairement à un simple
    /// Replace(property.name, ...) qui peut matcher au mauvais endroit) et retourne true si sa
    /// valeur correspond à AU MOINS UNE des `values` fournies.</summary>
    private bool MatchesAny(SerializedProperty property, string fieldName, object[] values)
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
