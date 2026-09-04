using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// =============================================================
// CRAFTRECIPEUI — Fiche recette + quantité (ouverte depuis CraftPanelUI)
// Path : Assets/Scripts/UI/PanelSecondaire/CraftRecipeUI.cs
// AetherTree GDD v3.6 — §13.2
//
// Flow : CraftPanelUI (liste) → bouton Craft → CraftRecipeUI (cette fiche,
// résultat + ingrédients qui se recalculent avec le slider de quantité) →
// bouton Craft ICI → TransactionConfirmUI (confirmation finale, qty déjà
// choisie ici pré-remplie). Le craft réel ne part que depuis la confirmation.
//
// Reste ouvert derrière TransactionConfirmUI (comme ShopUI reste ouvert
// derrière son propre popup) — pas fermé automatiquement à l'ouverture du
// popup, seulement via son propre bouton Close ou en cascade depuis
// CraftPanelUI.Close()/PNJWindowUI.Close().
// =============================================================

public class CraftRecipeUI : MonoBehaviour
{
    public static CraftRecipeUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject panel;

    [Header("Header")]
    public TextMeshProUGUI titleText;
    public Button          closeButton;

    [Header("Résultat")]
    public Image           itemIcon;
    public TextMeshProUGUI itemNameText;
    public TextMeshProUGUI itemDescText;

    [Header("Ingrédients")]
    public Transform  ingredientsContent;
    public GameObject ingredientLinePrefab; // "RessourceNeed" — Icon / RessourceName / Qty

    [Header("Quantité")]
    public TMP_InputField quantityInput;
    public Slider          quantitySlider;
    public Button          decrementButton; // "-" — quantité - 1
    public Button          incrementButton; // "+" — quantité + 1
    public Button          minButton;       // "Min" — saute à 1
    public Button          maxButton;       // "Max" — saute au plafond fabricable (stock actuel)

    [Header("Action")]
    public Button craftButton; // "ResultCraftButton" — ouvre TransactionConfirmUI

    private const string ColorOk  = "#4CDB57"; // même famille que ForgeUI
    private const string ColorBad = "#E0455F";

    private RecipeData _recipe;
    private int         _quantity = 1;
    private int         _maxQty   = 1;

    private readonly List<GameObject> _ingredientLines = new List<GameObject>();

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        closeButton?.onClick.AddListener(Close);
        craftButton?.onClick.AddListener(OnCraftButtonClicked);
        decrementButton?.onClick.AddListener(() => SetQuantity(_quantity - 1));
        incrementButton?.onClick.AddListener(() => SetQuantity(_quantity + 1));
        minButton?.onClick.AddListener(() => SetQuantity(1));
        maxButton?.onClick.AddListener(() => SetQuantity(_maxQty));

        if (quantitySlider != null)
            quantitySlider.onValueChanged.AddListener(v => SetQuantity(Mathf.RoundToInt(v)));

        if (quantityInput != null)
        {
            quantityInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            quantityInput.onEndEdit.AddListener(OnQuantityTextEdited);
        }

        Close();
    }

    // =========================================================
    // OPEN / CLOSE
    // =========================================================

    public void Open(RecipeData recipe)
    {
        if (recipe == null || CraftSystem.Instance == null) return;
        _recipe = recipe;
        _maxQty = CraftSystem.Instance.GetMaxCraftable(recipe);
        if (_maxQty <= 0) return; // rien à afficher — le bouton Craft de la liste ne devrait déjà plus être cliquable dans ce cas

        if (panel != null) panel.SetActive(true);

        var result = recipe.result;
        var lang   = LocalizationManager.CurrentLanguage;
        SetText(titleText, result != null ? result.displayName.Get(lang) : recipe.name);
        SetText(itemNameText, result != null ? result.displayName.Get(lang) : recipe.name);
        SetText(itemDescText, result != null ? result.description.Get(lang) : "");
        if (itemIcon != null) itemIcon.sprite = result?.icon;

        if (quantitySlider != null)
        {
            quantitySlider.minValue = 1;
            quantitySlider.maxValue = _maxQty;
        }

        SetQuantity(1);
    }

    /// <summary>Annule une canalisation en cours (rien n'est consommé) avant de fermer —
    /// même garde que ForgeUI.Close()/CraftPanelUI.Close().</summary>
    public void Close()
    {
        if (CraftSystem.Instance != null && CraftSystem.Instance.IsChanneling)
            ProgressBarUI.Instance?.Cancel();

        if (panel != null) panel.SetActive(false);
        _recipe = null;
        ClearIngredientLines();
    }

    // =========================================================
    // QUANTITÉ
    // =========================================================

    private void SetQuantity(int value)
    {
        _quantity = Mathf.Clamp(value, 1, _maxQty);
        quantitySlider?.SetValueWithoutNotify(_quantity);
        quantityInput?.SetTextWithoutNotify(_quantity.ToString());
        RefreshIngredients();
    }

    private void OnQuantityTextEdited(string text)
    {
        if (!int.TryParse(text, out int value)) value = 1;
        SetQuantity(value);
    }

    // =========================================================
    // INGRÉDIENTS
    // =========================================================

    /// <summary>Une ligne PAR ingrédient, recalculée à chaque changement de quantité —
    /// même logique rouge/vert que CraftPanelUI/ForgeUI. Bouton Craft actif seulement si
    /// TOUS les ingrédients sont suffisants pour la quantité choisie.</summary>
    private void RefreshIngredients()
    {
        if (_recipe == null) return;

        ClearIngredientLines();
        bool allEnough = _recipe.ingredients.Count > 0;

        if (ingredientsContent != null && ingredientLinePrefab != null)
        {
            foreach (var ing in _recipe.ingredients)
            {
                if (ing?.item == null) continue;

                int  have   = InventorySystem.Instance?.GetItemCount(ing.item) ?? 0;
                int  needed = ing.quantity * _quantity;
                bool enough = have >= needed;
                if (!enough) allEnough = false;

                var line = Instantiate(ingredientLinePrefab, ingredientsContent);
                _ingredientLines.Add(line);

                var icon = line.transform.Find("Icon")?.GetComponent<Image>();
                if (icon != null) icon.sprite = ing.item.icon;

                var nameTMP = line.transform.Find("RessourceName")?.GetComponent<TextMeshProUGUI>();
                if (nameTMP != null) nameTMP.text = ing.item.displayName.Get(LocalizationManager.CurrentLanguage);

                var qtyTMP = line.transform.Find("Qty")?.GetComponent<TextMeshProUGUI>();
                if (qtyTMP != null)
                {
                    string hex = enough ? ColorOk : ColorBad;
                    qtyTMP.text = $"<color={hex}>{have}/{needed}</color>";
                }
            }
        }

        if (craftButton != null) craftButton.interactable = allEnough;
    }

    private void ClearIngredientLines()
    {
        foreach (var go in _ingredientLines) if (go != null) Destroy(go);
        _ingredientLines.Clear();
    }

    // =========================================================
    // CRAFT — ouvre la confirmation finale
    // =========================================================

    private void OnCraftButtonClicked()
    {
        if (_recipe == null) return;

        string itemName = _recipe.result != null
            ? _recipe.result.displayName.Get(LocalizationManager.CurrentLanguage)
            : _recipe.name;

        var recipe = _recipe;
        TransactionConfirmUI.Instance?.OpenCraftFlow(itemName, _maxQty,
            onConfirm: quantity => StartCraft(recipe, quantity),
            initialQty: _quantity);
    }

    private void StartCraft(RecipeData recipe, int quantity)
    {
        if (craftButton != null) craftButton.interactable = false;

        CraftSystem.Instance.StartCraft(recipe, quantity,
            onComplete: OnCraftResolved,
            onCancel:   RefreshIngredients);
    }

    /// <summary>Après un craft réussi, le stock d'ingrédients a changé — recalcule le
    /// plafond de quantité (peut avoir baissé, voire tomber à 0 si tout est épuisé).</summary>
    private void OnCraftResolved()
    {
        if (_recipe != null && CraftSystem.Instance != null)
        {
            _maxQty = CraftSystem.Instance.GetMaxCraftable(_recipe);
            if (_maxQty <= 0)
            {
                Close();
            }
            else
            {
                if (quantitySlider != null) quantitySlider.maxValue = _maxQty;
                SetQuantity(Mathf.Min(_quantity, _maxQty));
            }
        }

        CraftPanelUI.Instance?.RefreshList();
    }

    private void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null) label.text = value ?? "";
    }
}
