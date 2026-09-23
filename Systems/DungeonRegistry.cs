using UnityEngine;
using System.Collections.Generic;

// =============================================================
// DUNGEONREGISTRY.CS — Registre de tous les DungeonData du projet
// Path : Assets/Scripts/Systems/DungeonRegistry.cs
// AetherTree GDD v3.6
//
// Même patron que CraftSystem.allRecipes / UnlockManager.allConditions —
// auto-fill éditeur, résolution par ID à l'exécution.
// =============================================================

public class DungeonRegistry : MonoBehaviour
{
    public static DungeonRegistry Instance { get; private set; }

    [Tooltip("Clic droit → 'Auto-remplir allDungeons' pour scanner le projet.")]
    public List<DungeonData> allDungeons = new List<DungeonData>();

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
#if UNITY_EDITOR
        EditorAutoFill();
#endif
    }

    public DungeonData Resolve(string dungeonID)
    {
        return allDungeons.Find(d => d != null && d.dungeonID == dungeonID);
    }

#if UNITY_EDITOR
    [ContextMenu("Auto-remplir allDungeons")]
    private void EditorAutoFill()
    {
        allDungeons.Clear();
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:DungeonData");
        foreach (string guid in guids)
        {
            var d = UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonData>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (d != null) allDungeons.Add(d);
        }
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
