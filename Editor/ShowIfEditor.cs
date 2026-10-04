#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Reflection;

// =============================================================
// SHOWIFEDITOR.CS — Editor de base générique qui respecte [ShowIf] sur TOUS les champs, y
// compris List<>/array.
// Path : Assets/Scripts/Editor/ShowIfEditor.cs
//
// Pourquoi ce fichier existe : confirmé par log diagnostique le 2026-10-04 (zéro appel à
// GetPropertyHeight pour aucune List<>/array, alors que les champs scalaires fonctionnent
// parfaitement) — Unity N'INVOQUE JAMAIS le PropertyDrawer attaché à un PropertyAttribute
// (ShowIfPropertyDrawer) pour un champ List<>/array dans l'Inspector PAR DÉFAUT (sans
// CustomEditor). C'est une limitation Unity, pas un bug de ShowIfPropertyDrawer — ce drawer
// continue de couvrir correctement tous les champs scalaires (bool/enum/float/ObjectReference).
//
// Pour une liste, impossible de compter sur le drawer pour la masquer via sa hauteur — il n'est
// simplement jamais appelé. Cette classe contourne le problème autrement : elle dessine chaque
// propriété ELLE-MÊME (boucle manuelle sur serializedObject), et n'appelle
// EditorGUILayout.PropertyField() QUE si ShowIfEvaluator confirme le champ visible — que ce soit
// un scalaire ou une liste. Masqué = jamais appelé = jamais dessiné, peu importe le type.
//
// Usage : [CustomEditor(typeof(MonScriptableObject))]
//         public class MonScriptableObjectEditor : ShowIfEditor {}
// (aucune méthode à override — hérite du rendu complet. Voir Editor/PNJDataEditor.cs.)
// =============================================================
public class ShowIfEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty prop = serializedObject.GetIterator();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;

            if (prop.propertyPath == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(prop, true);
                continue;
            }

            FieldInfo field = target.GetType().GetField(prop.name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var showIf = field?.GetCustomAttribute<ShowIfAttribute>();

            if (showIf != null && !ShowIfEvaluator.IsVisible(prop, showIf))
                continue; // caché — ni header, ni champ, exactement comme un scalaire masqué

            // Header conditionnel dessiné ICI uniquement pour les List<>/array — un champ
            // scalaire visible passe normalement par ShowIfPropertyDrawer (confirmé
            // fonctionnel), qui dessine DÉJÀ son propre Header ; le redessiner ici le
            // dupliquerait. Une liste visible, elle, ne passe JAMAIS par ce drawer (la
            // limitation Unity ci-dessus) — personne d'autre ne dessine son Header, donc il
            // faut le faire ici, seulement pour elle.
            if (showIf != null && !string.IsNullOrEmpty(showIf.Header) &&
                prop.isArray && prop.propertyType != SerializedPropertyType.String)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(showIf.Header, EditorStyles.boldLabel);
            }

            EditorGUILayout.PropertyField(prop, true);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
