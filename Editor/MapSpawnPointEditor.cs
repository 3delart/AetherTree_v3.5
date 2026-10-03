#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

// =============================================================
// MAPSPAWNPOINTEDITOR.CS — Inspector de MapSpawnPoint
// Path : Assets/Scripts/Editor/MapSpawnPointEditor.cs
//
// Même principe que le bouton "Générer" de PortalEditor : remplit spawnID avec
// "Spawn_{NomDeLaScène}" ET renomme le GameObject du même nom dans la Hierarchy. Si un autre
// MapSpawnPoint de la même scène porte déjà cet ID (points nommés multiples), un suffixe est
// ajouté (_2, _3...) pour garder l'ID unique. Un clic explicite, jamais automatique : un ID
// retapé à la main n'est jamais écrasé sans action de ta part.
// =============================================================
[CustomEditor(typeof(MapSpawnPoint))]
public class MapSpawnPointEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("type"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("isDefault"));

        var idProp = serializedObject.FindProperty("spawnID");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Spawn ID", GUILayout.Width(EditorGUIUtility.labelWidth));
        idProp.stringValue = EditorGUILayout.TextField(idProp.stringValue);

        var point = (MapSpawnPoint)target;
        bool canGenerate = point.gameObject.scene.IsValid();
        using (new EditorGUI.DisabledScope(!canGenerate))
        {
            if (GUILayout.Button("Générer", GUILayout.Width(70)))
            {
                string generated = GenerateUniqueID(point);
                idProp.stringValue = generated;

                Undo.RecordObject(point.gameObject, "Renommer MapSpawnPoint");
                point.gameObject.name = generated;
            }
        }
        EditorGUILayout.EndHorizontal();

        serializedObject.ApplyModifiedProperties();
    }

    private static string GenerateUniqueID(MapSpawnPoint self)
    {
        string baseID = $"Spawn_{self.gameObject.scene.name}";
        string candidate = baseID;
        int suffix = 2;

        while (IsTakenByAnother(self, candidate))
        {
            candidate = $"{baseID}_{suffix}";
            suffix++;
        }
        return candidate;
    }

    private static bool IsTakenByAnother(MapSpawnPoint self, string id)
    {
        foreach (var other in Object.FindObjectsOfType<MapSpawnPoint>(true))
        {
            if (other != self && other.gameObject.scene == self.gameObject.scene && other.spawnID == id)
                return true;
        }
        return false;
    }
}
#endif
