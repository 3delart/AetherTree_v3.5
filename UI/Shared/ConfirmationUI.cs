using UnityEngine;
using UnityEngine.UI;
using TMPro;

// =============================================================
// CONFIRMATIONUI — Popup de confirmation partagé (1 écran)
// Path : Assets/Scripts/UI/Shared/ConfirmationUI.cs
// AetherTree GDD v3.6 — §13.2
//
// Renommé depuis TransactionConfirmUI (2026-09-29, Florian) — a dépassé son
// scope d'origine (achat/vente boutique) dès l'ajout d'OpenCraftFlow puis
// OpenConfirmFlow (confirmation binaire générique, ex: quitter un donjon en
// cours). Le nom ne reflétait plus l'usage réel.
//
// Un seul écran : titre dynamique ("Acheter"/"Vendre"/"Fabriquer"/libre),
// nom/message, quantité éditable (optionnelle), prix total (recalculé en
// live, optionnel) — Confirmer/Annuler. Rien n'est débité/retiré avant le
// clic Confirmer — onConfirm(quantity) n'est appelé qu'à ce moment-là,
// Annuler ne fait rien.
//
// Pas de canalisation (ProgressBarUI) — réservée à Craft/Upgrade/Pari, pas
// aux transactions boutique classiques.
//
// Réutilisable par CHAQUE futur onglet Boutique (Merchant, Forge,
// Cuisinier...), pas seulement Merchant — un seul composant partagé.
//
// Aussi réutilisé par CraftPanelUI (OpenCraftFlow) — même écran, mais la
// ligne de prix (Aeris) est masquée : une RecipeData n'a pas de coût, juste
// des ingrédients déjà affichés sur la carte de recette elle-même.
//
// Et par OpenConfirmFlow — toute décision binaire oui/non sans quantité ni
// prix (ex: quitter un donjon en cours, voir InstanceSession.LeaveDungeon).
// =============================================================

public class ConfirmationUI : MonoBehaviour
{
    public static ConfirmationUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject panel;

    [Header("Contenu")]
    public TextMeshProUGUI titleText;      // "Acheter" / "Vendre"
    public TextMeshProUGUI itemNameText;
    public TMP_InputField  quantityInput;
    public TextMeshProUGUI priceText;

    [Header("Boutons")]
    public Button confirmButton;
    public Button cancelButton;

    private int    _unitPrice;
    private int    _maxQty;
    private int    _quantity;
    private bool   _showPrice;
    private bool   _showQuantity;
    private System.Action<int> _onConfirm;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        confirmButton?.onClick.AddListener(OnConfirmClicked);
        cancelButton?.onClick.AddListener(Close);

        if (quantityInput != null)
        {
            quantityInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            quantityInput.onEndEdit.AddListener(OnQuantityEdited);
        }
        Close();
    }

    /// <summary>Ouvre en mode Achat — titre "Acheter".</summary>
    public void OpenBuyFlow(string itemName, int unitPrice, int maxQty, System.Action<int> onConfirm)
        => Open("Acheter", itemName, unitPrice, maxQty, onConfirm, showPrice: true, initialQty: 1, showQuantity: true);

    /// <summary>Ouvre en mode Vente — titre "Vendre".</summary>
    public void OpenSellFlow(string itemName, int unitPrice, int maxQty, System.Action<int> onConfirm)
        => Open("Vendre", itemName, unitPrice, maxQty, onConfirm, showPrice: true, initialQty: 1, showQuantity: true);

    /// <summary>Ouvre en mode Craft — titre "Fabriquer", pas de ligne de prix (une recette
    /// n'a pas de coût Aeris, ses ingrédients sont déjà affichés sur la carte de recette).
    /// maxQty = quantité maximum fabricable avec le stock d'ingrédients actuel. initialQty =
    /// quantité déjà choisie en amont (ex: slider de CraftRecipeUI), pré-remplie ici.</summary>
    public void OpenCraftFlow(string itemName, int maxQty, System.Action<int> onConfirm, int initialQty = 1)
        => Open("Fabriquer", itemName, 0, maxQty, onConfirm, showPrice: false, initialQty, showQuantity: true);

    /// <summary>Ouvre en confirmation simple oui/non — ni quantité (toujours 1, jamais éditable)
    /// ni prix. Pour toute décision binaire (entrer dans une instance, quitter un groupe...) qui
    /// n'a pas de sens à passer par le flow Acheter/Vendre/Fabriquer.</summary>
    public void OpenConfirmFlow(string title, string message, System.Action<int> onConfirm)
        => Open(title, message, 0, 1, onConfirm, showPrice: false, initialQty: 1, showQuantity: false);

    private void Open(string title, string itemName, int unitPrice, int maxQty, System.Action<int> onConfirm, bool showPrice, int initialQty, bool showQuantity)
    {
        _unitPrice    = unitPrice;
        _maxQty       = Mathf.Clamp(maxQty, 1, 99); // 99 = plafond absolu, quel que soit le stock/la quantité possédée
        _quantity     = Mathf.Clamp(initialQty, 1, _maxQty);
        _showPrice    = showPrice;
        _showQuantity = showQuantity;
        _onConfirm    = onConfirm;

        if (panel != null) panel.SetActive(true);
        SetText(titleText,    title);
        SetText(itemNameText, itemName);
        quantityInput?.gameObject.SetActive(_showQuantity);
        quantityInput?.SetTextWithoutNotify(_quantity.ToString());
        RefreshPrice();
    }

    public void Close()
    {
        if (panel != null) panel.SetActive(false);
        _onConfirm = null;
    }

    private void OnQuantityEdited(string text)
    {
        if (!int.TryParse(text, out int value)) value = 1;
        _quantity = Mathf.Clamp(value, 1, _maxQty);
        quantityInput?.SetTextWithoutNotify(_quantity.ToString());
        RefreshPrice();
    }

    private void RefreshPrice()
    {
        if (priceText == null) return;
        priceText.gameObject.SetActive(_showPrice);
        if (!_showPrice) return;

        int total = _unitPrice * _quantity;
        priceText.text = $"{_unitPrice} × {_quantity} = {total} ¤";
    }

    private void OnConfirmClicked()
    {
        // Dernière valeur tapée pas forcément commit si le joueur n'a pas quitté le champ.
        if (quantityInput != null) OnQuantityEdited(quantityInput.text);

        var callback = _onConfirm;
        int quantity = _quantity;
        Close();
        callback?.Invoke(quantity);
    }

    private void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null) label.text = value ?? "";
    }
}
