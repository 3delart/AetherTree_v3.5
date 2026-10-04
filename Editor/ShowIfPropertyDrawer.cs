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
// NE COUVRE QUE LES CHAMPS SCALAIRES — confirmé par log diagnostique le 2026-10-04 : Unity
// n'invoque JAMAIS ce drawer pour un champ List<>/array dans l'Inspector par défaut (limitation
// Unity, pas un bug de ce fichier). Pour masquer une liste conditionnellement, voir
// ShowIfEditor.cs — classe de base d'Editor réutilisable qui contourne le problème en dessinant
// chaque propriété elle-même plutôt que de compter sur ce drawer pour les champs List<>/array.
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
        {
            // Un List<T>/array laissé DÉPLIÉ (property.isExpanded = true, réglé par un clic
            // précédent sur le foldout à un moment où le champ était visible) garde sa rangée
            // "Add/Remove" + éléments dessinée par Unity en interne même quand cette méthode
            // renvoie une hauteur négative — la hauteur seule ne suffit pas à cacher un tableau
            // dont le foldout est resté ouvert. Le replier explicitement force le masquage,
            // peu importe son état précédent. Sans effet sur un champ non-tableau (isExpanded
            // reste toujours false pour ceux-là, ce Set est un no-op).
            property.isExpanded = false;
            return -EditorGUIUtility.standardVerticalSpacing; // annule l'espacement auto entre champs
        }

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

    private bool IsVisible(SerializedProperty property) =>
        ShowIfEvaluator.IsVisible(property, (ShowIfAttribute)attribute);
}
#endif
