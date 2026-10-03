#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

// =============================================================
// MAPINFOEDITOR.CS — Inspector de MapInfo
// Path : Assets/Scripts/Editor/MapInfoEditor.cs
//
// La liste "Biomes" ne montre par défaut que la référence à chaque BiomeZone (Unity n'expose
// jamais les champs d'un component référencé dans une liste) — Display Name reste invisible et
// non éditable depuis là. Ce fichier ajoute une section sous la liste avec un champ texte par
// BiomeZone déjà glissé, pour éditer son nom directement depuis MapInfo sans cliquer sur chaque
// instance dans la Hierarchy.
// =============================================================
[CustomEditor(typeof(MapInfo))]
public class MapInfoEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("palier"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("biomes"), true);

        var mapInfo = (MapInfo)target;
        if (mapInfo.biomes != null && mapInfo.biomes.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Noms des biomes", EditorStyles.boldLabel);

            foreach (var biome in mapInfo.biomes)
            {
                if (biome == null) continue;

                EditorGUI.BeginChangeCheck();
                string newName = EditorGUILayout.TextField(biome.gameObject.name, biome.displayName);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(biome, "Renommer BiomeZone");
                    biome.displayName = newName;
                    EditorUtility.SetDirty(biome);
                }
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
