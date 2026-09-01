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
            EditorGUI.BeginProperty(position, label, property);
            property.floatValue = EditorGUI.Slider(position, label, property.floatValue, range.min, range.max);
            EditorGUI.EndProperty();
            return;
        }
        if (rangeAttr.Length > 0 && property.propertyType == SerializedPropertyType.Integer)
        {
            var range = (RangeAttribute)rangeAttr[0];
            EditorGUI.BeginProperty(position, label, property);
            property.intValue = EditorGUI.IntSlider(position, label, property.intValue, (int)range.min, (int)range.max);
            EditorGUI.EndProperty();
            return;
        }

        var minAttr = fieldInfo.GetCustomAttributes(typeof(MinAttribute), false);
        if (minAttr.Length > 0 && property.propertyType == SerializedPropertyType.Float)
        {
            var min = (MinAttribute)minAttr[0];
            EditorGUI.BeginProperty(position, label, property);
            property.floatValue = Mathf.Max(min.min, EditorGUI.FloatField(position, label, property.floatValue));
            EditorGUI.EndProperty();
            return;
        }
        if (minAttr.Length > 0 && property.propertyType == SerializedPropertyType.Integer)
        {
            var min = (MinAttribute)minAttr[0];
            EditorGUI.BeginProperty(position, label, property);
            property.intValue = Mathf.Max((int)min.min, EditorGUI.IntField(position, label, property.intValue));
            EditorGUI.EndProperty();
            return;
        }

        EditorGUI.PropertyField(position, property, label, true);
    }

    private bool IsVisible(SerializedProperty property)
    {
        var showIf = (ShowIfAttribute)attribute;

        // Résout le champ frère par chemin relatif — robuste dans les listes/objets
        // imbriqués (ex: "effects.Array.data[0].effectType"), contrairement à un
        // simple Replace(property.name, ...) qui peut matcher au mauvais endroit.
        int lastDot = property.propertyPath.LastIndexOf('.');
        string siblingPath = lastDot >= 0
            ? property.propertyPath.Substring(0, lastDot + 1) + showIf.conditionField
            : showIf.conditionField;

        SerializedProperty condition = property.serializedObject.FindProperty(siblingPath);
        if (condition == null) return true; // champ introuvable — n'échoue pas silencieusement en masquant tout

        foreach (var value in showIf.values)
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
