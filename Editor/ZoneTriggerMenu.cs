#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

// =============================================================
// ZONETRIGGERMENU.CS — Crée le prefab ZoneTrigger (biome/zone) prêt à glisser dans une scène
// Path : Assets/Scripts/Editor/ZoneTriggerMenu.cs
//
// Menu : AetherTree > Créer Prefab ZoneTrigger
// À exécuter UNE fois pour créer le prefab GÉNÉRIQUE de départ. Crée
// Assets/Prefabs/Prefab_ZoneTrigger.prefab (SphereCollider en trigger, rayon 3 — petit trigger
// fonctionnel, pas une région de map) + ZoneTrigger ; s'il existe déjà, le sélectionne. Pour
// CHAQUE zone réelle : dupliquer ce prefab (ou en faire une Variant) dans Assets/Prefabs/Zones/,
// ajuster la taille du collider à la zone voulue, remplir zoneID/isOutdoor/isDungeon/isPvP
// directement sur CETTE copie — un prefab PAR zone depuis le 2026-10-04 (plus de ZoneData séparé),
// glissé en scène ET référencé par ConditionData/ZoneChecker ou QuestObjective (objectif Explore)
// qui pointent DIRECTEMENT sur ce prefab. Sert UNIQUEMENT aux conditions (AFK/temps passé dans la
// zone, voir ZoneChecker) — pour nommer une région de map sur la minimap, voir BiomeZone/BiomeData.
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
