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
    private static DungeonRegistry _instance;

    /// <summary>Auto-crée une instance si aucune n'a été placée en scène — évite le plantage
    /// silencieux (ResolveByKey renvoie null via ?., même message que "clé pas référencée")
    /// quand on oublie de poser le GameObject. Ne dispense PAS de placer+remplir un
    /// DungeonRegistry dans une scène persistante pour un vrai build : AssetDatabase (utilisé
    /// par EditorAutoFill) n'existe qu'en Éditeur, ce fallback ne peut scanner le projet que
    /// pendant du Play Mode DANS l'Éditeur, jamais en build.</summary>
    public static DungeonRegistry Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("DungeonRegistry (auto)");
                _instance = go.AddComponent<DungeonRegistry>();
            }
            return _instance;
        }
    }

    [Tooltip("Clic droit → 'Auto-remplir allDungeons' pour scanner le projet.")]
    public List<DungeonData> allDungeons = new List<DungeonData>();

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
#if UNITY_EDITOR
        EditorAutoFill();
#endif
    }

    public DungeonData Resolve(string dungeonID)
    {
        return allDungeons.Find(d => d != null && d.dungeonID == dungeonID);
    }

    /// <summary>Résolution inverse — quel DungeonData référence cette clé (ConsumableData
    /// requiredKey) ? Utilisé par ConsoBarUI.TryUseSlot() quand une DungeonKey est consommée.</summary>
    public DungeonData ResolveByKey(ConsumableData key)
    {
        return allDungeons.Find(d => d != null && d.requiredKey == key);
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
