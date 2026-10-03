#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

// =============================================================
// PORTALEDITOR.CS — Inspector dédié à Portal
// Path : Assets/Scripts/Editor/PortalEditor.cs
//
// [ShowIf] (Utils/ShowIfAttribute.cs) marche très bien sur les champs
// scalaires (linkedDungeon, targetTier) mais PAS sur un List<T>
// (requiredMobs, requiredActivatables, requiredQuests) — même limitation Unity connue que
// DungeonMapData (voir Editor/DungeonMapDataPropertyDrawer.cs) : un
// PropertyDrawer basé sur un attribut posé sur une liste est appliqué à
// CHAQUE ÉLÉMENT, jamais au foldout de la liste elle-même. Ce fichier
// dessine tout l'Inspector à la main pour que la section "Verrou" (les 3
// blocs par gateType, listes de conditions incluses) se masque
// correctement.
// =============================================================
[CustomEditor(typeof(Portal))]
public class PortalEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Pas de headers manuels ici — [Header(...)] sur les champs eux-mêmes (Portal.cs) les
        // dessine déjà tout seul quand on les PropertyField, les dupliquer ferait doublon.
        DrawPortalIDWithGenerate(serializedObject.FindProperty("portalID"));

        DrawSceneField(serializedObject.FindProperty("targetMapScene"), "Target Map Scene");

        var portal = (Portal)target;
        // Mirroir de la convention "{scène courante}_to_{scène cible}" (Générer côté portalID) :
        // le portail d'en face suit la même convention → "{scène cible}_to_{scène courante}",
        // calculable sans que ce portail existe déjà.
        string currentSceneName = portal.gameObject.scene.IsValid() ? portal.gameObject.scene.name : null;
        string generatedMirror = (portal.targetMapScene != null && !string.IsNullOrEmpty(currentSceneName))
            ? $"{portal.targetMapScene.name}_to_{currentSceneName}"
            : null;
        DrawPortalIDField(serializedObject.FindProperty("targetPortalID"), "Target Portal ID", generatedMirror);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("spawnCooldown"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("teleportDelay"));

        var gateTypeProp = serializedObject.FindProperty("gateType");
        EditorGUILayout.PropertyField(gateTypeProp);
        var gateType = (PortalGateType)gateTypeProp.enumValueIndex;

        switch (gateType)
        {
            case PortalGateType.RequiresDungeonEntry:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("linkedDungeon"));
                break;
            case PortalGateType.RequiresTierUnlock:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("targetTier"));
                break;
            case PortalGateType.RequiresConditions:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("requiredMobs"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("requiredActivatables"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("showActivatableCount"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("minPlayerLevel"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("requiredQuests"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("minPrestigeRank"));
                break;
        }

        EditorGUILayout.PropertyField(serializedObject.FindProperty("isHidden"));

        EditorGUILayout.PropertyField(serializedObject.FindProperty("lockedVisual"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("openVisual"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("dungeonVisual"));

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>Champ portalID + bouton "Générer" — remplit "{scène courante}_to_{scène cible}",
    /// unique tant qu'un seul portail de CETTE scène mène à CETTE cible (le cas courant), ET
    /// renomme le GameObject en "Portal_{id}" dans la Hierarchy (facile à repérer, cohérent avec
    /// le vrai identifiant du portail). Ne s'auto-remplit JAMAIS tout seul (contrairement à
    /// targetMap/linkedInstanceID) : un clic explicite, pour ne jamais écraser un ID/nom retapé
    /// à la main pour le cas rare de collision (2 portails de la même scène vers la même cible).</summary>
    private void DrawPortalIDWithGenerate(SerializedProperty prop)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Portal ID", GUILayout.Width(EditorGUIUtility.labelWidth));
        prop.stringValue = EditorGUILayout.TextField(prop.stringValue);

        var portal = (Portal)target;
        bool canGenerate = portal.targetMapScene != null;
        using (new EditorGUI.DisabledScope(!canGenerate))
        {
            if (GUILayout.Button("Générer", GUILayout.Width(70)))
            {
                string sceneName = portal.gameObject.scene.IsValid() ? portal.gameObject.scene.name : "Scene";
                string generatedID = $"{sceneName}_to_{portal.targetMapScene.name}";
                prop.stringValue = generatedID;

                Undo.RecordObject(portal.gameObject, "Renommer le portail");
                portal.gameObject.name = $"Portal_{generatedID}";
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>Champ SceneAsset + bouton "▾" listant UNIQUEMENT les scènes du Build Settings —
    /// évite le sélecteur Unity générique qui montre TOUS les .unity du projet, scènes de test
    /// isolées comprises. Reste un champ objet normal en dessous : glisser-déposer marche
    /// toujours si la scène visée n'est pas (encore) dans le Build Settings.</summary>
    private static void DrawSceneField(SerializedProperty prop, string label)
    {
        EditorGUILayout.BeginHorizontal();
        // Label dessiné à la main + champ sans son propre label — évite le retour à la ligne
        // que PropertyField(prop, GUIContent) déclenche parfois dans un BeginHorizontal étroit
        // (largeur insuffisante calculée en interne pour label + box + bouton).
        EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth));
        prop.objectReferenceValue = EditorGUILayout.ObjectField(
            prop.objectReferenceValue, typeof(SceneAsset), false);
        if (GUILayout.Button("▾", GUILayout.Width(24)))
        {
            var menu = new GenericMenu();
            var scenes = EditorBuildSettings.scenes;
            bool any = false;
            foreach (var scene in scenes)
            {
                if (!scene.enabled || string.IsNullOrEmpty(scene.path)) continue;
                var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);
                if (sceneAsset == null) continue;

                any = true;
                var targetProp = prop; // capture locale pour la closure
                bool isCurrent = prop.objectReferenceValue == sceneAsset;
                menu.AddItem(new GUIContent(sceneAsset.name), isCurrent, () =>
                {
                    targetProp.serializedObject.Update();
                    targetProp.objectReferenceValue = sceneAsset;
                    targetProp.serializedObject.ApplyModifiedProperties();
                });
            }
            if (!any)
                menu.AddDisabledItem(new GUIContent("(aucune scène activée dans le Build Settings)"));
            menu.ShowAsContext();
        }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>Champ texte + "Générer" (calcule "{scène cible}_to_{scène courante}" — le
    /// mirroir exact de Générer côté portalID, en PURE convention de nommage, sans avoir besoin
    /// que le portail d'en face existe déjà).</summary>
    private static void DrawPortalIDField(SerializedProperty prop, string label, string generatedMirror)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth));
        prop.stringValue = EditorGUILayout.TextField(prop.stringValue);

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(generatedMirror)))
        {
            if (GUILayout.Button("Générer", GUILayout.Width(70)))
                prop.stringValue = generatedMirror;
        }
        EditorGUILayout.EndHorizontal();
    }
}
#endif
