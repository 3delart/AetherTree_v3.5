#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

// =============================================================
// ZONETRIGGERMENU.CS — Crée le prefab ZoneTrigger (biome/zone) prêt à glisser dans une scène
// Path : Assets/Scripts/Editor/ZoneTriggerMenu.cs
//
// Menu : AetherTree > Créer Prefab ZoneTrigger
// À exécuter UNE fois. Crée Assets/Prefabs/Prefab_ZoneTrigger.prefab (SphereCollider en trigger,
// rayon 3 — petit trigger fonctionnel, pas une région de map) + ZoneTrigger ; s'il existe déjà, le
// sélectionne. Glisser ensuite le prefab dans une scène, ajuster la taille à la zone voulue, et
// assigner un ZoneData. Sert UNIQUEMENT aux conditions (AFK/temps passé dans la zone, voir
// ZoneChecker) — pour nommer une région de map sur la minimap, voir BiomeZone/BiomeData à la place.
// =============================================================
public static class ZoneTriggerMenu
{
    private const string PrefabPath = "Assets/Prefabs/Prefab_ZoneTrigger.prefab";

    [MenuItem("AetherTree/Créer Prefab ZoneTrigger")]
    public static void CreatePrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("[ZoneTriggerMenu] Prefab_ZoneTrigger existe déjà — sélectionné, rien créé.");
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        var go = new GameObject("Prefab_ZoneTrigger");
        var collider = go.AddComponent<SphereCollider>();
        collider.isTrigger = true;
        collider.radius = 3f;
        go.AddComponent<ZoneTrigger>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"[ZoneTriggerMenu] Prefab créé : {PrefabPath}");
    }
}
#endif
