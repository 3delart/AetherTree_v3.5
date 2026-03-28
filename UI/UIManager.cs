using System.Collections.Generic;
using UnityEngine;

// =============================================================
// UIMANAGER.CS — Gestionnaire central des panels UI
// Path : Assets/Scripts/UI/UIManager.cs
// AetherTree GDD v30 — Section 40
// =============================================================
// SETUP :
//   1. Placer ce script sur un GameObject "UIManager" dans la scène
//   2. Assigner chaque panel dans l'Inspector
//   3. Les panels "bloquants" empêchent les clics monde (ex: Inventory)
//   4. Les panels "overlay" s'affichent par-dessus sans bloquer
//
// PANELS SECONDAIRES (ouvrir/fermer) :
//   Inventory, Character, SkillLibrary, Quests, Social, Settings,
//   Map, Recipe (panel unique avec onglets Equipment / Potion / Cuisine)
//
// RACCOURCIS CLAVIER : définis dans GameControls / KeyBindings
//   Escape → ferme tous les panels secondaires ouverts
// =============================================================

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    // ── Panels secondaires bloquants ─────────────────────────
    [Header("Panels secondaires — Bloquants (bloquent les clics monde)")]
    public GameObject inventoryPanel;
    public GameObject characterPanel;
    public GameObject skillLibraryPanel;
    public GameObject questsPanel;
    public GameObject socialPanel;
    public GameObject settingsPanel;
    public GameObject mapPanel;
    public GameObject recipePanel;      // Panel unique — onglets gérés par RecipeUI
    public GameObject statPointPanel;   // Panel StatPoints §3.2.1

    // ── Panels secondaires overlay (non bloquants) ───────────
    [Header("Panels secondaires — Overlay (non bloquants)")]
    public List<GameObject> overlayPanels = new List<GameObject>();

    // ── Panels fixes (référence pour HideAll si besoin) ──────
    [Header("Panels fixes (référence uniquement — jamais cachés par HideAll)")]
    public List<GameObject> fixedPanels = new List<GameObject>();

    // ── Runtime ──────────────────────────────────────────────
    private Dictionary<string, GameObject> _panelMap;

    // ─────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        BuildPanelMap();
        HideAllSecondary();
    }

    private void Update()
    {
        HandleKeyboardShortcuts();
    }

    // =========================================================
    // RACCOURCIS CLAVIER
    // =========================================================

    private void HandleKeyboardShortcuts()
    {
        // Escape — ferme tous les panels secondaires ouverts
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            HideAllSecondary();
            return;
        }

        if (GameControls.OpenInventory)    TogglePanel("Inventory");
        if (GameControls.OpenCharacter)    TogglePanel("Character");
        if (GameControls.OpenSkillLibrary) TogglePanel("SkillLibrary");
        if (GameControls.OpenQuests)       TogglePanel("Quests");
        if (GameControls.OpenSocial)       TogglePanel("Social");
        if (GameControls.OpenSettings)     TogglePanel("Settings");
        if (GameControls.OpenMap)          TogglePanel("Map");
        if (GameControls.OpenRecipe)       TogglePanel("Recipe");
        if (GameControls.OpenStatPoints)   TogglePanel("StatPoints");

        // Onglets Social — ouvrent SocialPanel sur l'onglet correspondant
        if (GameControls.OpenMail)  SocialUI.Instance?.ToggleTab(SocialUI.SocialTab.Mail);
        if (GameControls.OpenChat)  SocialUI.Instance?.ToggleTab(SocialUI.SocialTab.Chat);
        if (GameControls.OpenGuild) SocialUI.Instance?.ToggleTab(SocialUI.SocialTab.Guild);
    }

    // =========================================================
    // API PUBLIQUE
    // =========================================================

    /// <summary>Ouvre/ferme un panel par son nom.</summary>
    public void TogglePanel(string panelName)
    {
        // Les panels avec leur propre UI controller utilisent Toggle()
        // pour déclencher leur Refresh() au bon moment.
        string key = panelName.ToLower();
        switch (key)
        {
            case "character":
                CharacterPanelUI.Instance?.Toggle();
                return;
            case "inventory":
                InventoryUI.Instance?.Toggle();
                return;
            case "quests":
                QuestJournalUI.Instance?.Toggle();
                return;
            case "statpoints":
                StatPointUI.Instance?.gameObject.SetActive(
                    StatPointUI.Instance != null && !StatPointUI.Instance.gameObject.activeSelf);
                if (StatPointUI.Instance != null && StatPointUI.Instance.gameObject.activeSelf)
                    StatPointUI.Instance.Refresh();
                return;
            
        }

        var panel = FindPanel(panelName);
        if (panel == null) { Debug.LogWarning($"[UIManager] Panel introuvable : {panelName}"); return; }
        panel.SetActive(!panel.activeSelf);
    }

    /// <summary>Ouvre un panel.</summary>
    public void ShowPanel(string panelName)
    {
        string key = panelName.ToLower();
        switch (key)
        {
            case "character":
                CharacterPanelUI.Instance?.Open();
                return;
            case "inventory":
                InventoryUI.Instance?.Open();
                return;
            case "quests":                     
                QuestJournalUI.Instance?.Toggle();
                return;
        }

        var panel = FindPanel(panelName);
        if (panel == null) { Debug.LogWarning($"[UIManager] Panel introuvable : {panelName}"); return; }
        panel.SetActive(true);
    }

    /// <summary>Ferme un panel.</summary>
    public void HidePanel(string panelName)
    {
        string key = panelName.ToLower();
        switch (key)
        {
            case "character":
                CharacterPanelUI.Instance?.Close();
                return;
            case "inventory":
                InventoryUI.Instance?.Close();
                return;
        }

        var panel = FindPanel(panelName);
        if (panel == null) { Debug.LogWarning($"[UIManager] Panel introuvable : {panelName}"); return; }
        panel.SetActive(false);
    }

    /// <summary>Ferme tous les panels secondaires (bloquants + overlay).</summary>
    public void HideAllSecondary()
    {
        // Panels avec controller propre — utilise Close() pour ne pas casser l'Instance
        CharacterPanelUI.Instance?.Close();
        InventoryUI.Instance?.Close();
        QuestJournalUI.Instance?.Close();   // ← AJOUTER
        if (StatPointUI.Instance != null)
            StatPointUI.Instance.gameObject.SetActive(false);


        // Panels sans controller — SetActive directement
        foreach (var kvp in _panelMap)
        {
            if (kvp.Key == "character" || kvp.Key == "inventory") continue;
            if (kvp.Value != null) kvp.Value.SetActive(false);
        }

        foreach (var p in overlayPanels)
            if (p != null) p.SetActive(false);
    }

    /// <summary>True si au moins un panel bloquant est ouvert — bloque les clics monde.</summary>
    public bool BlocksWorldInput()
    {
        foreach (var kvp in _panelMap)
            if (kvp.Value != null && kvp.Value.activeSelf) return true;
        return false;
    }

    /// <summary>Alias — compatibilité PlayerController.</summary>
    public bool IsAnyPanelOpen() => BlocksWorldInput();

    /// <summary>True si un panel spécifique est ouvert.</summary>
    public bool IsPanelOpen(string panelName)
    {
        var panel = FindPanel(panelName);
        return panel != null && panel.activeSelf;
    }

    // =========================================================
    // INTERNE
    // =========================================================

    private GameObject FindPanel(string name)
    {
        if (_panelMap.TryGetValue(name.ToLower(), out var panel)) return panel;

        foreach (var p in overlayPanels)
            if (p != null && p.name.ToLower().Contains(name.ToLower())) return p;

        return null;
    }

    private void BuildPanelMap()
    {
        _panelMap = new Dictionary<string, GameObject>
        {
            { "inventory",    inventoryPanel    },
            { "character",    characterPanel    },
            { "skilllibrary", skillLibraryPanel },
            //{ "quests",       questsPanel       },
            { "social",       socialPanel       },
            { "settings",     settingsPanel     },
            { "map",          mapPanel          },
            { "recipe",       recipePanel       },
            { "statpoints",   statPointPanel    },
        };
    }
}