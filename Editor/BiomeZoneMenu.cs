#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

// =============================================================
// BIOMEZONEMENU.CS — Crée le prefab BiomeZone (zone de map) prêt à glisser dans une scène
// Path : Assets/Scripts/Editor/BiomeZoneMenu.cs
//
// Menu : AetherTree > Créer Prefab BiomeZone
// À exécuter UNE fois. Crée Assets/Prefabs/Prefab_BiomeZone.prefab (SphereCollider en trigger,
// rayon 15 — une région de map, pas un petit trigger fonctionnel comme ZoneTrigger) + BiomeZone ;
// s'il existe déjà, le sélectionne. Glisser ensuite dans une scène, ajuster la taille à la région
// voulue, remplir Display Name, puis glisser l'instance dans MapInfo.biomes de la même scène.
// =============================================================
public static class BiomeZoneMenu
{
    private const string PrefabPath = "Assets/Prefabs/Prefab_BiomeZone.prefab";

    [MenuItem("AetherTree/Créer Prefab BiomeZone")]
    public static void CreatePrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("[BiomeZoneMenu] Prefab_BiomeZone existe déjà — sélectionné, rien créé.");
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        var go = new GameObject("Prefab_BiomeZone");
        var collider = go.AddComponent<SphereCollider>();
        collider.isTrigger = true;
        collider.radius = 15f;
        go.AddComponent<BiomeZone>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"[BiomeZoneMenu] Prefab créé : {PrefabPath}");
    }
}
#endif
