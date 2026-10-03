#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// =============================================================
// AUTOSAVESCENES.CS — Sauvegarde automatique des scènes ouvertes
// Path : Assets/Scripts/Editor/AutoSaveScenes.cs
//
// Unity n'a aucun autosave de scène natif — après 2 pertes de travail sur ce
// projet (plantage Editor ayant corrompu des assets, puis perte de la scène
// DungeonPartyPanel), ce script sauvegarde les scènes ouvertes à intervalle
// régulier. Désactivé pendant le Play Mode (sauver l'état runtime muté
// écraserait la scène avec des valeurs temporaires) et silencieux si rien
// n'a changé depuis la dernière sauvegarde (pas de save inutile).
//
// Toggle et intervalle : menu AetherTree > Auto-Save.
// =============================================================
[InitializeOnLoad]
public static class AutoSaveScenes
{
    private const string EnabledKey  = "AetherTree.AutoSave.Enabled";
    private const string IntervalKey = "AetherTree.AutoSave.IntervalMinutes";
    private const float  DefaultIntervalMinutes = 5f;

    private static double _lastSaveTime;

    static AutoSaveScenes()
    {
        _lastSaveTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += Update;
    }

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(EnabledKey, true);
        set => EditorPrefs.SetBool(EnabledKey, value);
    }

    private static float IntervalMinutes
    {
        get => EditorPrefs.GetFloat(IntervalKey, DefaultIntervalMinutes);
        set => EditorPrefs.SetFloat(IntervalKey, value);
    }

    private static void Update()
    {
        if (!Enabled || EditorApplication.isPlaying || EditorApplication.isCompiling) return;

        double elapsedMinutes = (EditorApplication.timeSinceStartup - _lastSaveTime) / 60.0;
        if (elapsedMinutes < IntervalMinutes) return;

        _lastSaveTime = EditorApplication.timeSinceStartup;

        if (!AnySceneDirty()) return;

        EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[AutoSave] Scènes sauvegardées automatiquement ({System.DateTime.Now:HH:mm:ss}).");
    }

    private static bool AnySceneDirty()
    {
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (EditorSceneManager.GetSceneAt(i).isDirty) return true;
        return false;
    }

    [MenuItem("AetherTree/Auto-Save/Activé", true)]
    private static bool ValidateToggleEnabled()
    {
        Menu.SetChecked("AetherTree/Auto-Save/Activé", Enabled);
        return true;
    }

    [MenuItem("AetherTree/Auto-Save/Activé")]
    private static void ToggleEnabled() => Enabled = !Enabled;

    [MenuItem("AetherTree/Auto-Save/Intervalle 2 min")]
    private static void SetInterval2() => IntervalMinutes = 2f;

    [MenuItem("AetherTree/Auto-Save/Intervalle 5 min")]
    private static void SetInterval5() => IntervalMinutes = 5f;

    [MenuItem("AetherTree/Auto-Save/Intervalle 10 min")]
    private static void SetInterval10() => IntervalMinutes = 10f;
}
#endif
