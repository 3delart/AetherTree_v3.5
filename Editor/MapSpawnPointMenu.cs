#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

// =============================================================
// MAPSPAWNPOINTMENU.CS — Crée le prefab MapSpawnPoint prêt à glisser dans une scène
// Path : Assets/Scripts/Editor/MapSpawnPointMenu.cs
//
// Menu : AetherTree > Créer Prefab MapSpawnPoint
// À exécuter UNE fois. Crée Assets/Prefabs/Prefab_MapSpawnPoint.prefab (GameObject vide +
// MapSpawnPoint, isDefault = true) ; s'il existe déjà, le sélectionne. Glisser ensuite le prefab
// dans chaque scène de map, à l'endroit d'apparition voulu.
// =============================================================
public static class MapSpawnPointMenu
{
    private const string PrefabPath = "Assets/Prefabs/Prefab_MapSpawnPoint.prefab";

    [MenuItem("AetherTree/Créer Prefab MapSpawnPoint")]
    public static void CreatePrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("[MapSpawnPointMenu] Prefab_MapSpawnPoint existe déjà — sélectionné, rien créé.");
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        var go = new GameObject("Prefab_MapSpawnPoint");
        go.AddComponent<MapSpawnPoint>();
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"[MapSpawnPointMenu] Prefab créé : {PrefabPath}");
    }
}
#endif
