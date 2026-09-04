using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// =============================================================
// CRAFTPANELUI — Panel de craft partagé (7 stations)
// Path : Assets/Scripts/UI/PanelSecondaire/CraftPanelUI.cs
// AetherTree GDD v3.6 — §13.2
//
// Un seul panel réutilisé pour les 7 CraftStationType — le shape d'écran est
// identique, seule la liste de recettes filtrées change (voir
// PNJWindowUI.ToStation()).
//
// UI = 1 seule liste (pas de vue liste/détail séparée) : chaque recette a sa
// propre carte (prefab "SelectedOutline") qui affiche déjà son nom ET tous
// ses ingrédients (icône + qté possédée/requise, rouge/vert) directement,
// sans clic supplémentaire. Cliquer une carte la sélectionne (surlignage).
// Le bouton Craft en bas est unique, global, agit sur la carte sélectionnée.
//
// Quantité de craft : le bouton Craft ouvre CraftRecipeUI (fiche recette avec
// slider de quantité), qui ouvre à son tour TransactionConfirmUI pour la
// confirmation finale. Cette liste-ci ne prévisualise que have/needed pour
// 1 unité (PreviewQuantity) — juste pour savoir si une carte est craftable
// au moins une fois, la vraie quantité se choisit dans CraftRecipeUI.
// =============================================================

public class CraftPanelUI : MonoBehaviour
{
    public static CraftPanelUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject panel;

    [Header("Header")]
    public TextMeshProUGUI titleText;
    public Button          closeButton;

    [Header("Liste de recettes")]
    public Transform  gridContent;
    public GameObject entryPrefab;         // racine "SelectedOutline" — voir Panel/PanelRecipeName/recipename et Panel/Scroll View/Viewport/Content à l'intérieur
    public GameObject ingredientLinePrefab; // "PrefabRecipeRessource" — RessourceIcon + Qty

    [Header("Action")]
    public Button craftButton; // unique, global — agit sur la recette sélectionnée

    private const string ColorOk  = "#4CDB57"; // même famille que ForgeUI
    private const string ColorBad = "#E0455F";

    // Utilisée uniquement pour la prévisualisation have/needed dans la liste (1 unité) et
    // pour savoir si la carte est cliquable/craftable au moins une fois — la vraie quantité
    // à fabriquer vient du popup TransactionConfirmUI (voir OnCraftClicked).
    private const int PreviewQuantity = 1;

    private PNJData          _pnjData;
    private Player           _player;
    private CraftStationType _station;

    private RecipeData _selectedRecipe;
    private GameObject _selectedEntry;

    private readonly List<GameObject> _entries = new List<GameObject>();

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        closeButton?.onClick.AddListener(Close);
        craftButton?.onClick.AddListener(OnCraftClicked);
        Close();
    }

    // =========================================================
    // OPEN / CLOSE
    // =========================================================

    public void Open(PNJData pnjData, Player player, CraftStationType station)
    {
        if (pnjData == null || player == null) return;
        _pnjData = pnjData;
        _player  = player;
        _station = station;

        if (panel != null) panel.SetActive(true);
        SetText(titleText, GetStationLabel(station));
        RefreshList();
    }

    /// <summary>Annule une canalisation en cours (rien n'est consommé) avant de fermer —
    /// même garde que ForgeUI.Close().</summary>
    public void Close()
    {
        if (CraftSystem.Instance != null && CraftSystem.Instance.IsChanneling)
            ProgressBarUI.Instance?.Cancel();

        CraftRecipeUI.Instance?.Close();

        if (panel != null) panel.SetActive(false);
        InventoryUI.Instance?.Close();

        _pnjData        = null;
        _player         = null;
        _selectedRecipe = null;
        _selectedEntry  = null;
        ClearGrid();
    }

    // =========================================================
    // LISTE — 1 carte par recette, ingrédients affichés en direct
    // =========================================================

    /// <summary>Public — appelé par CraftRecipeUI après un craft pour resynchroniser les
    /// have/needed affichés sur les cartes (le sien inclus, s'il est toujours sélectionné).</summary>
    public void RefreshList()
    {
        var previouslySelected = _selectedRecipe;
        ClearGrid();
        _selectedRecipe = null;
        _selectedEntry  = null;
        if (craftButton != null) craftButton.interactable = false;

        if (_pnjData == null || _player == null || entryPrefab == null || gridContent == null) return;
        if (CraftSystem.Instance == null) return;

        foreach (var recipe in CraftSystem.Instance.allRecipes)
        {
            if (recipe == null || recipe.station != _station) continue;
            if (!_player.unlockedRecipes.Contains(recipe)) continue;

            var entry = SpawnEntry(recipe);
            if (recipe == previouslySelected) SelectEntry(recipe, entry);
        }
    }

    private GameObject SpawnEntry(RecipeData recipe)
    {
        var go = Instantiate(entryPrefab, gridContent);
        _entries.Add(go);

        var nameTMP = go.transform.Find("Panel/PanelRecipeName/recipename")?.GetComponent<TextMeshProUGUI>();
        if (nameTMP != null)
            nameTMP.text = recipe.result != null
                ? recipe.result.displayName.Get(LocalizationManager.CurrentLanguage)
                : recipe.name;

        var ingredientsContent = go.transform.Find("Panel/Scroll View/Viewport/Content");

        if (ingredientsContent != null && ingredientLinePrefab != null)
        {
            foreach (var ing in recipe.ingredients)
            {
                if (ing?.item == null) continue;

                int  have   = InventorySystem.Instance?.GetItemCount(ing.item) ?? 0;
                int  needed = ing.quantity * PreviewQuantity;
                bool enough = have >= needed;

                var line = Instantiate(ingredientLinePrefab, ingredientsContent);

                var icon = line.transform.Find("RessourceIcon")?.GetComponent<Image>();
                if (icon != null) icon.sprite = ing.item.icon;

                var qtyTMP = line.transform.Find("Qty")?.GetComponent<TextMeshProUGUI>();
                if (qtyTMP != null)
                {
                    string hex = enough ? ColorOk : ColorBad;
                    qtyTMP.text = $"<color={hex}>{have}/{needed}</color>";
                }
            }
        }

        SetEntrySelected(go, false);

        var btn = go.GetComponent<Button>() ?? go.GetComponentInChildren<Button>();
        var capturedRecipe = recipe;
        var capturedEntry  = go;
        if (btn != null) btn.onClick.AddListener(() => SelectEntry(capturedRecipe, capturedEntry));
        else Debug.LogWarning($"[CRAFT-UI] Aucun Button trouvé sur '{go.name}' — ajoute un component Button " +
                               "sur la racine 'SelectedOutline' (ou un enfant) pour rendre la carte cliquable.");

        return go;
    }

    private void ClearGrid()
    {
        foreach (var go in _entries) if (go != null) Destroy(go);
        _entries.Clear();
    }

    // =========================================================
    // SÉLECTION
    // =========================================================

    private void SelectEntry(RecipeData recipe, GameObject entry)
    {
        if (_selectedEntry != null) SetEntrySelected(_selectedEntry, false);
        _selectedRecipe = recipe;
        _selectedEntry  = entry;
        SetEntrySelected(entry, true);

        if (craftButton != null)
            craftButton.interactable = CraftSystem.Instance != null && CraftSystem.Instance.CanCraft(recipe, PreviewQuantity);
    }

    /// <summary>Surlignage — joue sur l'alpha (pas Image.enabled, qui retirerait la carte du
    /// raycast et casserait le clic) de l'Image portée par la racine "SelectedOutline"
    /// elle-même (pas un enfant séparé, contrairement à ShopUI).</summary>
    private void SetEntrySelected(GameObject entry, bool selected)
    {
        var outline = entry.GetComponent<Image>();
        if (outline == null) return;
        var c = outline.color;
        outline.color = new Color(c.r, c.g, c.b, selected ? 1f : 0f);
    }

    // =========================================================
    // CRAFT
    // =========================================================

    /// <summary>Ouvre CraftRecipeUI (fiche recette + quantité) — la confirmation finale
    /// (TransactionConfirmUI) est déclenchée depuis CE panel-là, pas ici.</summary>
    private void OnCraftClicked()
    {
        if (_selectedRecipe == null) return;
        CraftRecipeUI.Instance?.Open(_selectedRecipe);
    }

    // =========================================================
    // HELPERS
    // =========================================================

    private string GetStationLabel(CraftStationType station) => station switch
    {
        CraftStationType.CraftWeaponArmor  => "Craft Équipement",
        CraftStationType.CraftFoodPotion   => "Cuisiner",
        CraftStationType.CraftDecor        => "Bricoler",
        CraftStationType.CraftHelmet       => "Craft Casque",
        CraftStationType.CraftIntermediate => "Station de Craft",
        CraftStationType.CraftGlovesBoots  => "Craft Gants/Bottes",
        CraftStationType.CraftJewelry      => "Craft Bijoux",
        _                                   => station.ToString(),
    };

    private void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null) label.text = value ?? "";
    }
}
