#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

// =============================================================
// DUNGEONGROUPPANELBUILDER.CS — Crée le panel de donjon dans la scène ouverte
// Path : Assets/Scripts/Editor/DungeonGroupPanelBuilder.cs
//
// Menu : AetherTree > UI > Créer DungeonGroupPanel
//
// Construit toute la hiérarchie décrite en tête de UI/PanelFixe/DungeonGroupPanelUI.cs
// (script sur un GameObject TOUJOURS actif + PanelRoot enfant distinct) et assigne tous les
// champs du script. Réutilise le prefab de ligne participant de Florian s'il existe
// (Assets/Prefabs/UI/ParticipanRow.prefab — 3 champs nommés PlayerLevel/PlayerName/PlayerLives,
// lus PAR NOM par DungeonGroupPanelUI.RefreshParticipants(), pas par ordre), lui ajoute un
// Button s'il n'en a pas encore (nécessaire pour le clic-cible). Sans ce prefab, en génère un
// nouveau au même format. Le panel est posé sur le Canvas du HUD — celui qui porte
// PlayerInfosPanel s'il existe, sinon le premier Canvas Screen Space de la scène — donc la scène
// contenant ce Canvas (_Persistent) doit être ouverte dans l'Editor.
//
// À exécuter UNE fois. Une seconde exécution ne crée rien : elle sélectionne le panel existant.
// Position/taille/couleurs sont ensuite librement modifiables dans l'Inspector — ce script ne
// sert qu'à éviter de tout câbler à la main (utile aussi pour reconstruire vite après une perte
// de scène, ex: crash Editor — voir Editor/AutoSaveScenes.cs pour éviter que ça se reproduise).
// =============================================================
public static class DungeonGroupPanelBuilder
{
    private const string PrefabFolder      = "Assets/Prefabs/UI";
    private const string FlorianRowPrefab  = PrefabFolder + "/ParticipanRow.prefab";
    private const string FallbackRowPrefab = PrefabFolder + "/Prefab_DungeonParticipantRow.prefab";

    private static readonly Color PanelBg   = new Color(0.106f, 0.118f, 0.145f, 0.94f);
    private static readonly Color TextMain  = new Color(0.91f, 0.90f, 0.87f, 1f);
    private static readonly Color TextMuted = new Color(0.55f, 0.58f, 0.64f, 1f);
    private static readonly Color ButtonBg  = new Color(0.14f, 0.15f, 0.20f, 1f);

    [MenuItem("AetherTree/UI/Créer DungeonGroupPanel")]
    public static void Build()
    {
        var existing = Object.FindObjectOfType<DungeonGroupPanelUI>(true);
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            Debug.Log("[DungeonGroupPanelBuilder] Un DungeonGroupPanelUI existe déjà dans la scène — sélectionné, rien créé.");
            return;
        }

        Canvas canvas = FindHudCanvas();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Canvas introuvable",
                "Aucun Canvas Screen Space trouvé dans les scènes ouvertes.\n\n" +
                "Ouvre la scène qui contient ton HUD (_Persistent) en plus de la scène courante " +
                "(clic droit sur la scène dans le Project > Open Scene Additive), puis relance.",
                "OK");
            return;
        }

        GameObject rowPrefab = GetOrCreateRowPrefab();

        // ── Racine — porte le script, TOUJOURS active ─────────
        var root = new GameObject("DungeonPartyPanel", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(root, "Créer DungeonGroupPanel");
        root.transform.SetParent(canvas.transform, false);
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(1f, 0.5f);
        rootRect.pivot = new Vector2(1f, 0.5f);
        rootRect.anchoredPosition = new Vector2(-12f, 0f);
        rootRect.sizeDelta = new Vector2(300f, 0f);

        var panel = root.AddComponent<DungeonGroupPanelUI>();

        // ── PanelRoot — fond + contenu, activé/désactivé par le script ──
        var panelRoot = new GameObject("PanelRoot", typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panelRoot.transform.SetParent(root.transform, false);
        var panelRect = (RectTransform)panelRoot.transform;
        panelRect.anchorMin = new Vector2(0f, 0.5f);
        panelRect.anchorMax = new Vector2(1f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = Vector2.zero;
        panelRoot.GetComponent<Image>().color = PanelBg;
        ConfigureVertical(panelRoot.GetComponent<VerticalLayoutGroup>(), 10, 8f);
        panelRoot.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var header = CreateText(panelRoot.transform, "HeaderText", 18, FontStyles.Bold, TextMain, "Nom du donjon");

        var footer = CreateText(panelRoot.transform, "FooterText", 13, FontStyles.Normal, TextMuted,
            "Vies  ♥♥♥♥♥");

        var rowsParent = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
        rowsParent.transform.SetParent(panelRoot.transform, false);
        ConfigureVertical(rowsParent.GetComponent<VerticalLayoutGroup>(), 0, 6f);

        var objectives = CreateText(panelRoot.transform, "ObjectivesText", 14, FontStyles.Normal, TextMain,
            "<b>Objectifs</b>  1 / 2\n[x] Ennemis vaincus  3 / 3\n[ ] Résoudre le mécanisme");

        var button = CreateButton(panelRoot.transform, "QuitGroup", "Quitter le groupe");

        // ── Assignation des champs ────────────────────────────
        Undo.RecordObject(panel, "Assigner DungeonGroupPanelUI");
        panel.panelRoot             = panelRoot;
        panel.headerText            = header;
        panel.participantRowPrefab  = rowPrefab;
        panel.participantRowsParent = rowsParent.transform;
        panel.objectivesText        = objectives;
        panel.footerText            = footer;
        panel.leaveGroupButton      = button;
        EditorUtility.SetDirty(panel);

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);
        Debug.Log($"[DungeonGroupPanelBuilder] Panel créé sous le Canvas '{canvas.name}' " +
                  $"(scène '{canvas.gameObject.scene.name}') — pense à sauvegarder la scène (Ctrl+S). " +
                  "Position/taille modifiables sur l'objet DungeonPartyPanel (ancré à droite, centre).");
    }

    // ── Helpers ───────────────────────────────────────────────

    private static Canvas FindHudCanvas()
    {
        var infos = Object.FindObjectOfType<PlayerInfosPanel>(true);
        if (infos != null)
        {
            var c = infos.GetComponentInParent<Canvas>(true);
            if (c != null) return c.rootCanvas;
        }

        foreach (var c in Object.FindObjectsOfType<Canvas>(true))
        {
            if (c.isRootCanvas && c.renderMode != RenderMode.WorldSpace) return c;
        }
        return null;
    }

    private static void ConfigureVertical(VerticalLayoutGroup group, int padding, float spacing)
    {
        group.padding = new RectOffset(padding, padding, padding, padding);
        group.spacing = spacing;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, float size, FontStyles style,
        Color color, string sample)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = sample;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.enableWordWrapping = true;
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    private static Button CreateButton(Transform parent, string name, string label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = ButtonBg;
        go.GetComponent<LayoutElement>().preferredHeight = 30f;

        var text = CreateText(go.transform, "Label", 13, FontStyles.Bold, TextMain, label);
        text.alignment = TextAlignmentOptions.Center;
        var rect = (RectTransform)text.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        return go.GetComponent<Button>();
    }

    /// <summary>Réutilise Assets/Prefabs/UI/ParticipanRow.prefab (celui de Florian, 3 champs
    /// PlayerLevel/PlayerName/PlayerLives) s'il existe — lui ajoute juste un Button sur son
    /// root s'il n'en a pas déjà un (nécessaire pour le clic-cible, voir RefreshParticipants()).
    /// Sinon génère un nouveau prefab au même format (fallback, ex: premier setup sur une
    /// nouvelle machine sans l'asset).</summary>
    private static GameObject GetOrCreateRowPrefab()
    {
        var florianRow = AssetDatabase.LoadAssetAtPath<GameObject>(FlorianRowPrefab);
        if (florianRow != null)
        {
            if (florianRow.GetComponent<Button>() == null)
            {
                using (var editScope = new PrefabUtility.EditPrefabContentsScope(FlorianRowPrefab))
                {
                    var root = editScope.prefabContentsRoot;
                    if (root.GetComponent<Image>() == null) root.AddComponent<Image>().color = Color.clear;
                    root.AddComponent<Button>();
                }
                Debug.Log("[DungeonGroupPanelBuilder] Button ajouté sur ParticipanRow.prefab (clic-cible).");
            }
            return florianRow;
        }

        var existingFallback = AssetDatabase.LoadAssetAtPath<GameObject>(FallbackRowPrefab);
        if (existingFallback != null) return existingFallback;

        EnsureFolder("Assets/Prefabs");
        EnsureFolder(PrefabFolder);

        var row = new GameObject("Prefab_DungeonParticipantRow", typeof(RectTransform), typeof(Image),
            typeof(Button), typeof(HorizontalLayoutGroup));
        row.GetComponent<Image>().color = Color.clear;
        var group = row.GetComponent<HorizontalLayoutGroup>();
        group.spacing = 6f;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;

        CreateText(row.transform, "PlayerLevel", 13, FontStyles.Normal, TextMuted, "Lv.1");
        CreateText(row.transform, "PlayerName",  15, FontStyles.Bold,   TextMain,  "PlayerName");
        CreateText(row.transform, "PlayerLives", 15, FontStyles.Normal, TextMain,  "♥♥");

        var prefab = PrefabUtility.SaveAsPrefabAsset(row, FallbackRowPrefab);
        Object.DestroyImmediate(row);
        return prefab;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
#endif
