using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// =============================================================
// CRAFTJOURNALUI.CS — Journal des recettes connues
// Path : Assets/Scripts/UI/PanelSecondaire/CraftJournalUI.cs
// AetherTree GDD v3.6 — §13.2
//
// Trie par RecipeCategory (résultat), PAS par station — contrairement à
// CraftPanelUI (ouvert à un PNJ précis), ce journal s'ouvre n'importe où,
// donc trier par "qu'est-ce que je sais faire" (type d'objet) a plus de
// sens que par lieu de fabrication. Détail en LECTURE SEULE (icône/nom/
// station/ingrédients) — pas de bouton Fabriquer ici, le craft se fait
// depuis CraftPanelUI à la station. Pas de compteur de progression —
// toutes les conditions de déblocage restent invisibles au joueur.
//
// L'ouverture (HUD/raccourci) n'est pas câblée ici — Toggle()/Open()/Close()
// sont publics, à brancher sur un bouton par Florian (hors scope).
// =============================================================

public class CraftJournalUI : MonoBehaviour
{
    public static CraftJournalUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject panel;
    public Button     closeButton;

    [Header("Recherche")]
    public TMP_InputField searchInput;

    [Header("Onglets (par RecipeCategory)")]
    public Button tabEquipment;
    public Button tabConsumable;
    public Button tabResource;

    [Header("Grille de recettes")]
    public Transform  gridContent;
    public GameObject entryPrefab; // RecipeCard — IconBox/Icon, Name, Station, UnlockedTag

    [Header("Détail (lecture seule)")]
    public Image           detailIcon;
    public TextMeshProUGUI detailName;
    public TextMeshProUGUI detailStation;
    public TextMeshProUGUI detailUnlockedTag;
    public Transform       ingredientsContent;
    public GameObject      ingredientLinePrefab; // Icon, Name, Qty

    private const string ColorOk  = "#4CDB57"; // même famille que ForgeUI/CraftPanelUI
    private const string ColorBad = "#E0455F";

    private RecipeCategory _currentCategory = RecipeCategory.Equipment;
    private string         _searchQuery     = "";
    private bool            _isOpen          = false;
    private Player          _player;

    private readonly List<GameObject> _entries         = new List<GameObject>();
    private readonly List<GameObject> _ingredientLines = new List<GameObject>();

    private static readonly Color TAB_ACTIVE   = new Color(0.35f, 0.22f, 0.65f);
    private static readonly Color TAB_INACTIVE = new Color(0.15f, 0.15f, 0.25f);

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (panel != null) panel.SetActive(false);
    }

    private void Start()
    {
        _player = FindObjectOfType<Player>();

        tabEquipment?.onClick.AddListener(() => SwitchTab(RecipeCategory.Equipment));
        tabConsumable?.onClick.AddListener(() => SwitchTab(RecipeCategory.Consumable));
        tabResource?.onClick.AddListener(() => SwitchTab(RecipeCategory.Resource));

        closeButton?.onClick.AddListener(Toggle);

        if (searchInput != null)
            searchInput.onValueChanged.AddListener(OnSearchChanged);

        RefreshTabVisuals();
        ClearDetail();
    }

    // =========================================================
    // OPEN / CLOSE / TOGGLE
    // =========================================================

    public void Open()
    {
        _isOpen = true;
        if (_player == null) _player = FindObjectOfType<Player>();
        if (panel != null) panel.SetActive(true);
        RefreshTabVisuals();
        RefreshGrid();
        ClearDetail();
    }

    public void Close()
    {
        _isOpen = false;
        if (panel != null) panel.SetActive(false);
    }

    public void Toggle() { if (_isOpen) Close(); else Open(); }

    public bool IsOpen => _isOpen;

    /// <summary>Rafraîchit si le panel est ouvert. Appelé par Player.UnlockRecipe() après déblocage.</summary>
    public void RefreshIfOpen() { if (_isOpen) RefreshGrid(); }

    // =========================================================
    // ONGLETS & RECHERCHE
    // =========================================================

    private void SwitchTab(RecipeCategory category)
    {
        _currentCategory = category;
        RefreshTabVisuals();
        RefreshGrid();
        ClearDetail();
    }

    private void OnSearchChanged(string value)
    {
        _searchQuery = value ?? "";
        RefreshGrid();
    }

    private void RefreshTabVisuals()
    {
        SetTab(tabEquipment, RecipeCategory.Equipment);
        SetTab(tabConsumable, RecipeCategory.Consumable);
        SetTab(tabResource,   RecipeCategory.Resource);
    }

    private void SetTab(Button btn, RecipeCategory category)
    {
        if (btn == null) return;
        btn.image.color = _currentCategory == category ? TAB_ACTIVE : TAB_INACTIVE;
    }

    // =========================================================
    // GRILLE
    // =========================================================

    private void RefreshGrid()
    {
        ClearGrid();
        if (gridContent == null || entryPrefab == null || _player?.unlockedRecipes == null) return;

        string query = _searchQuery.Trim().ToLowerInvariant();

        foreach (var recipe in _player.unlockedRecipes)
        {
            if (recipe == null) continue;
            if (recipe.GetCategory() != _currentCategory) continue;

            if (!string.IsNullOrEmpty(query))
            {
                string name = GetRecipeName(recipe).ToLowerInvariant();
                if (!name.Contains(query)) continue;
            }

            SpawnRecipeEntry(recipe);
        }
    }

    private void SpawnRecipeEntry(RecipeData recipe)
    {
        var entry = Instantiate(entryPrefab, gridContent);
        _entries.Add(entry);

        var icon = entry.transform.Find("Icon")?.GetComponent<Image>();
        if (icon != null)
        {
            icon.sprite = recipe.result?.icon;
            icon.color  = recipe.result?.icon != null ? Color.white : new Color(0.3f, 0.3f, 0.3f, 0.8f);
        }

        var nameTMP = entry.transform.Find("Name")?.GetComponentInChildren<TextMeshProUGUI>();
        if (nameTMP != null) nameTMP.text = GetRecipeName(recipe);

        var stationTMP = entry.transform.Find("Station")?.GetComponentInChildren<TextMeshProUGUI>();
        if (stationTMP != null) stationTMP.text = GetStationLabel(recipe.station);

        var unlockedTag = entry.transform.Find("UnlockedTag")?.gameObject;
        if (unlockedTag != null) unlockedTag.SetActive(recipe.tier == RecipeTier.Unlocked);

        var btn = entry.GetComponent<Button>();
        if (btn != null)
        {
            var captured = recipe;
            btn.onClick.AddListener(() => ShowDetail(captured));
        }
    }

    private void ClearGrid()
    {
        foreach (var go in _entries) if (go != null) Destroy(go);
        _entries.Clear();
    }

    // =========================================================
    // DÉTAIL (lecture seule — pas de bouton Fabriquer, voir CraftPanelUI)
    // =========================================================

    private void ShowDetail(RecipeData recipe)
    {
        if (recipe == null) { ClearDetail(); return; }

        if (detailIcon != null)
        {
            detailIcon.sprite  = recipe.result?.icon;
            detailIcon.enabled = recipe.result?.icon != null;
        }
        if (detailName != null) detailName.text = GetRecipeName(recipe);
        if (detailStation != null) detailStation.text = GetStationLabel(recipe.station);
        if (detailUnlockedTag != null) detailUnlockedTag.gameObject.SetActive(recipe.tier == RecipeTier.Unlocked);

        ClearIngredientLines();
        if (ingredientsContent != null && ingredientLinePrefab != null)
        {
            foreach (var ing in recipe.ingredients)
            {
                if (ing?.item == null) continue;

                var go = Instantiate(ingredientLinePrefab, ingredientsContent);
                _ingredientLines.Add(go);

                var icon = go.transform.Find("Icon")?.GetComponent<Image>();
                if (icon != null) icon.sprite = ing.item.icon;

                var nameTMP = go.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
                if (nameTMP != null) nameTMP.text = ing.item.displayName.Get(LocalizationManager.CurrentLanguage);

                var qtyTMP = go.transform.Find("Qty")?.GetComponent<TextMeshProUGUI>();
                if (qtyTMP != null)
                {
                    int  have   = InventorySystem.Instance?.GetItemCount(ing.item) ?? 0;
                    bool enough = have >= ing.quantity;
                    string hex  = enough ? ColorOk : ColorBad;
                    qtyTMP.text = $"<color={hex}>{have}/{ing.quantity}</color>";
                }
            }
        }
    }

    private void ClearDetail()
    {
        if (detailIcon    != null) { detailIcon.sprite = null; detailIcon.enabled = false; }
        if (detailName    != null) detailName.text    = "";
        if (detailStation != null) detailStation.text = "";
        if (detailUnlockedTag != null) detailUnlockedTag.gameObject.SetActive(false);
        ClearIngredientLines();
    }

    private void ClearIngredientLines()
    {
        foreach (var go in _ingredientLines) if (go != null) Destroy(go);
        _ingredientLines.Clear();
    }

    // =========================================================
    // HELPERS
    // =========================================================

    private string GetRecipeName(RecipeData recipe) => recipe.result != null
        ? recipe.result.displayName.Get(LocalizationManager.CurrentLanguage)
        : recipe.name;

    private string GetStationLabel(CraftStationType station) => station switch
    {
        CraftStationType.CraftWeaponArmor  => "Forgeron",
        CraftStationType.CraftFoodPotion   => "Cuisinier",
        CraftStationType.CraftDecor        => "Bricoleur",
        CraftStationType.CraftHelmet       => "Tailleur",
        CraftStationType.CraftIntermediate => "Station de Craft",
        CraftStationType.CraftGlovesBoots  => "Cordonnier",
        CraftStationType.CraftJewelry      => "Bijoutier",
        _                                   => station.ToString(),
    };
}
