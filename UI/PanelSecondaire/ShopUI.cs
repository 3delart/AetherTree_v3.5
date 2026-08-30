using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// =============================================================
// SHOPUI — Interface de shop PNJ
// Path : Assets/Scripts/UI/PanelSecondaire/ShopUI.cs
// AetherTree GDD v30 — §19 / §14
// =============================================================

public class ShopUI : MonoBehaviour
{
    public static ShopUI Instance { get; private set; }

    [Header("Header")]
    public TextMeshProUGUI shopTitleText;
    public TextMeshProUGUI aerisText;
    public Button          closeButton;

    [Header("Onglets")]
    public Button tabBuyButton;
    public Button tabSellButton;

    [Header("Grille")]
    public Transform  shopGridContent;
    public GameObject shopCellPrefab;

    [Header("Panel Détail")]
    public GameObject           detailPanel;
    public Image                detailIcon;
    public TextMeshProUGUI      detailNameText;
    public TextMeshProUGUI      detailDescText;
    public TextMeshProUGUI      detailPriceText;
    public Button               btnMinus;
    public Button               btnPlus;
    public Button               btnMin;
    public Button               btnMax;
    public Slider               quantitySlider;
    public TMP_InputField       quantityInput;
    public Button               actionButton;
    public TextMeshProUGUI      actionButtonText;
    public TextMeshProUGUI      reputationWarning;

    private static readonly Color TAB_ACTIVE   = new Color(0.35f, 0.22f, 0.65f);
    private static readonly Color TAB_INACTIVE = new Color(0.15f, 0.15f, 0.25f);
    private static readonly float[] SELL_MULTIPLIERS = { 1.0f, 1.05f, 1.10f, 1.20f, 1.35f, 1.50f };

    private PNJData  _pnjData;
    private Player   _player;
    private ShopTab  _activeTab = ShopTab.Buy;
    private int      _quantity  = 1;

    private ShopEntry     _selectedEntry;
    private InventoryItem _selectedItem;

    private readonly List<GameObject> _cells = new List<GameObject>();

    private enum ShopTab { Buy, Sell }

    // =========================================================
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    private void Start()
    {
        closeButton?.onClick.AddListener(CloseShop);
        tabBuyButton?.onClick.AddListener(() => SwitchTab(ShopTab.Buy));
        tabSellButton?.onClick.AddListener(() => SwitchTab(ShopTab.Sell));
        btnMinus?.onClick.AddListener(() => ChangeQuantity(-1));
        btnPlus?.onClick.AddListener(() => ChangeQuantity(1));
        btnMin?.onClick.AddListener(() => SetQuantity(1));
        btnMax?.onClick.AddListener(() => SetQuantity(GetMaxQuantity()));
        actionButton?.onClick.AddListener(OnActionClicked);


        // Slider → met à jour la quantité
        if (quantitySlider != null)
        {
            quantitySlider.wholeNumbers = true;
            quantitySlider.minValue     = 1;
            quantitySlider.onValueChanged.AddListener(OnSliderChanged);
        }

        // InputField → met à jour la quantité
        if (quantityInput != null)
        {
            quantityInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            quantityInput.onEndEdit.AddListener(OnInputChanged);
        }

        if (AerisSystem.Instance != null)
            AerisSystem.Instance.OnAerisChanged += RefreshAeris;

        ClearDetail();
    }

    private void OnDestroy()
    {
        if (AerisSystem.Instance != null)
            AerisSystem.Instance.OnAerisChanged -= RefreshAeris;
    }

    // =========================================================
    // OPEN / CLOSE
    // =========================================================

    public void OpenShop(PNJData pnjData, Player player)
    {
        if (pnjData == null || player == null) return;
        _pnjData  = pnjData;
        _player   = player;
        _quantity = 1;

        gameObject.SetActive(true);
        if (shopTitleText != null) shopTitleText.text = pnjData.pnjName;

        RefreshAeris(AerisSystem.Instance?.Aeris ?? 0);
        SwitchTab(ShopTab.Buy);
        ClearDetail();

        // Ouvre l'inventaire du joueur automatiquement
        InventoryUI.Instance?.Open();
    }

    public void CloseShop()
    {
        gameObject.SetActive(false);
        _pnjData       = null;
        _player        = null;
        _selectedEntry = null;
        _selectedItem  = null;
        ClearGrid();
        ClearDetail();
    }

    // =========================================================
    // ONGLETS
    // =========================================================

    private void SwitchTab(ShopTab tab)
    {
        _activeTab     = tab;
        _selectedEntry = null;
        _selectedItem  = null;
        _quantity      = 1;

        if (tabBuyButton  != null) tabBuyButton .image.color = tab == ShopTab.Buy  ? TAB_ACTIVE : TAB_INACTIVE;
        if (tabSellButton != null) tabSellButton.image.color = tab == ShopTab.Sell ? TAB_ACTIVE : TAB_INACTIVE;

        ClearDetail();
        RefreshGrid();
    }

    // =========================================================
    // GRILLE
    // =========================================================

    private void RefreshGrid()
    {
        ClearGrid();
        if (_pnjData == null || shopCellPrefab == null || shopGridContent == null) return;

        if (_activeTab == ShopTab.Buy) BuildBuyGrid();
        else                           BuildSellGrid();
    }

    private void BuildBuyGrid()
    {
        var allEntries = new List<ShopEntry>();
        if (_pnjData.shopItems  != null) allEntries.AddRange(_pnjData.shopItems);
        if (_pnjData.shopSkills != null) allEntries.AddRange(_pnjData.shopSkills);

        foreach (ShopEntry entry in allEntries)
        {
            if (entry?.item == null) continue;

            bool reputationLocked = _player.worldReputationRank < entry.requiredWorldReputationRank;
            bool exhausted        = ShopStockRegistry.Instance != null
                                 && ShopStockRegistry.Instance.IsExhausted(_pnjData.pnjName, entry);
            bool locked           = reputationLocked || exhausted;

            Sprite icon = GetEntryIcon(entry);
            string name = GetEntryName(entry);
            string desc = GetEntryDesc(entry);

            // Label affiché sous l'icône
            string priceLabel = exhausted ? "Acheté" : null;

            var capturedEntry = entry;
            var capturedIcon  = icon;
            var capturedName  = name;
            var capturedDesc  = desc;

            SpawnCell(
                icon       : icon,
                price      : entry.aerisCost,
                locked     : locked,
                exhausted  : exhausted,
                priceLabel : priceLabel,
                onSelect   : () => SelectBuyEntry(capturedEntry, capturedIcon, capturedName, capturedDesc)
            );
        }
    }

    private void BuildSellGrid()
    {
        if (InventorySystem.Instance == null) return;

        foreach (InventoryItem item in InventorySystem.Instance.GetAllItems())
        {
            if (item == null) continue;
            int sellPrice = GetSellPrice(item);
            if (sellPrice <= 0) continue;

            var captured = item;
            SpawnCell(
                icon     : item.Icon,
                price    : sellPrice,
                locked   : false,
                onSelect : () => SelectSellItem(captured, sellPrice)
            );
        }
    }

    private void SpawnCell(Sprite icon, int price, bool locked, bool exhausted = false, string priceLabel = null, System.Action onSelect = null)
    {
        var go = Instantiate(shopCellPrefab, shopGridContent);
        _cells.Add(go);

        var iconImg = go.GetComponent<Image>();
        if (iconImg != null)
        {
            iconImg.sprite = icon;
            iconImg.color  = icon != null
                ? (locked ? new Color(0.35f, 0.35f, 0.35f, 1f) : Color.white)
                : new Color(0.2f, 0.2f, 0.2f, 0.8f);
        }

        var priceTMP = go.transform.Find("PriceLabel")?.GetComponent<TextMeshProUGUI>();
        if (priceTMP != null)
        {
            if (exhausted)
            {
                priceTMP.text  = "Acheté";
                priceTMP.color = new Color(0.5f, 0.5f, 0.5f);
            }
            else
            {
                priceTMP.text  = priceLabel ?? $"{price} ¤";
                priceTMP.color = locked ? new Color(0.5f, 0.5f, 0.5f) : new Color(1f, 0.85f, 0.2f);
            }
        }

        var lockOverlay = go.transform.Find("LockOverlay")?.GetComponent<Image>();
        if (lockOverlay != null)
            lockOverlay.gameObject.SetActive(locked && !exhausted);

        // Overlay "Acheté" — optionnel, si tu as un enfant "ExhaustedOverlay" dans le prefab
        var exhaustedOverlay = go.transform.Find("ExhaustedOverlay")?.GetComponent<Image>();
        if (exhaustedOverlay != null)
            exhaustedOverlay.gameObject.SetActive(exhausted);

        // Cherche le Button sur la racine OU dans les enfants
        var btn = go.GetComponent<Button>() ?? go.GetComponentInChildren<Button>();
        if (btn != null)
        {
            btn.interactable = !locked;
            btn.onClick.RemoveAllListeners();
            if (!locked && onSelect != null) btn.onClick.AddListener(() => onSelect());
        }
    }

    private void ClearGrid()
    {
        foreach (var go in _cells) if (go != null) Destroy(go);
        _cells.Clear();
    }

    // =========================================================
    // SÉLECTION
    // =========================================================

    private void SelectBuyEntry(ShopEntry entry, Sprite icon, string name, string desc)
    {
        _selectedEntry = entry;
        _selectedItem  = null;
        _quantity      = 1;

        if (detailPanel != null) detailPanel.SetActive(true);

        SetDetailIcon(icon);
        SetText(detailNameText,   name);
        SetText(detailDescText,   desc);
        SetText(actionButtonText, "Acheter");

        int max = !entry.isUnlimitedStock
            ? (ShopStockRegistry.Instance?.GetRemainingStock(_pnjData.pnjName, entry) ?? entry.stockCount)
            : 99;
        SetQuantityRange(max);
        SetQuantity(1);
        RefreshBuyPrice();
        HideReputationWarning();
    }

    private void SelectSellItem(InventoryItem item, int unitPrice)
    {
        _selectedItem  = item;
        _selectedEntry = null;

        if (detailPanel != null) detailPanel.SetActive(true);

        SetDetailIcon(item.Icon);
        SetText(detailNameText,   item.Name);
        SetText(detailDescText,   GetItemDescription(item));
        SetText(actionButtonText, "Vendre");

        int max = GetAvailableQuantity(item);
        SetQuantityRange(max);
        SetQuantity(1);
        RefreshSellPrice(unitPrice);
        HideReputationWarning();
    }

    // =========================================================
    // QUANTITÉ
    // =========================================================

    private void ChangeQuantity(int delta)
    {
        SetQuantity(_quantity + delta);
    }

    private void OnSliderChanged(float value)
    {
        int val = Mathf.RoundToInt(value);
        if (val == _quantity) return; // évite la boucle slider↔input
        SetQuantity(val);
    }

    private void OnInputChanged(string text)
    {
        if (int.TryParse(text, out int val))
            SetQuantity(val);
        else
            RefreshQuantityDisplay(); // remet la valeur valide
    }

    private void SetQuantity(int value)
    {
        int max = GetMaxQuantity();
        _quantity = Mathf.Clamp(value, 1, max);
        RefreshQuantityDisplay();

        if (_activeTab == ShopTab.Buy)  RefreshBuyPrice();
        else if (_selectedItem != null) RefreshSellPrice(GetSellPrice(_selectedItem));
    }

    private int GetMaxQuantity()
    {
        if (_activeTab == ShopTab.Buy && _selectedEntry != null && !_selectedEntry.isUnlimitedStock)
        {
            return ShopStockRegistry.Instance?.GetRemainingStock(_pnjData.pnjName, _selectedEntry)
                ?? _selectedEntry.stockCount;
        }
        if (_activeTab == ShopTab.Sell && _selectedItem != null)
            return GetAvailableQuantity(_selectedItem);
        return 99;
    }

    private void RefreshQuantityDisplay()
    {
        // InputField — évite de retriggerer OnInputChanged
        if (quantityInput != null && !quantityInput.isFocused)
            quantityInput.SetTextWithoutNotify(_quantity.ToString());

        // Slider — évite de retriggerer OnSliderChanged
        if (quantitySlider != null)
        {
            quantitySlider.SetValueWithoutNotify(_quantity);
        }
    }

    private void SetQuantityRange(int max)
    {
        if (quantitySlider != null)
        {
            quantitySlider.minValue = 1;
            quantitySlider.maxValue = Mathf.Max(1, max);
        }
    }

    private void RefreshBuyPrice()
    {
        if (_selectedEntry == null) return;
        int unit  = _selectedEntry.aerisCost;
        int total = unit * _quantity;
        bool canAfford = (AerisSystem.Instance?.Aeris ?? 0) >= total;
        SetText(detailPriceText, $"{unit} × {_quantity} = {total} ¤");
        if (detailPriceText != null)
            detailPriceText.color = canAfford ? new Color(1f, 0.85f, 0.2f) : new Color(0.9f, 0.3f, 0.3f);
        if (actionButton != null)
            actionButton.interactable = canAfford;
    }

    private void RefreshSellPrice(int unitPrice)
    {
        int total = unitPrice * _quantity;
        SetText(detailPriceText, $"{unitPrice} × {_quantity} = +{total} ¤");
        if (detailPriceText != null)
            detailPriceText.color = new Color(0.4f, 0.9f, 0.4f);
        // Toujours réactiver le bouton en mode Sell — RefreshBuyPrice peut l'avoir
        // désactivé si le joueur n'avait pas assez d'Aeris lors du dernier Buy.
        if (actionButton != null)
            actionButton.interactable = true;
    }

    // =========================================================
    // ACTION — Acheter / Vendre
    // =========================================================

    private void OnActionClicked()
    {
        if (_activeTab == ShopTab.Buy) BuySelected();
        else                           SellSelected();
    }

    private void BuySelected()
    {
        if (_selectedEntry == null || _player == null) return;

        int total = _selectedEntry.aerisCost * _quantity;
        if (!(AerisSystem.Instance?.Spend(total) ?? false))
        {
            Debug.Log("[SHOP] Aeris insuffisants.");
            return;
        }

        if (_selectedEntry.item is SkillData skill)
        {
            _player.UnlockSkill(skill);
            FloatingText.Spawn($"Skill débloqué !", _player.transform.position, new Color(0.6f, 0.4f, 1f));
        }
        else if (_selectedEntry.item is PermanentSkillData permanent)
        {
            _player.UnlockPermanent(permanent);
            FloatingText.Spawn($"Passif débloqué !", _player.transform.position, new Color(0.6f, 0.4f, 1f));
        }
        else
        {
            for (int i = 0; i < _quantity; i++)
            {
                InventoryItem item = ResolveItemFromEntry(_selectedEntry);
                if (item != null) InventorySystem.Instance?.AddItem(item);
            }
        }

        if (!_selectedEntry.isUnlimitedStock)
        {
            ShopStockRegistry.Instance?.RecordPurchase(_pnjData.pnjName, _selectedEntry, _quantity);
            _selectedEntry.stockCount -= _quantity;
            if (_selectedEntry.stockCount <= 0)
            {
                ClearDetail();
                RefreshGrid();
                return;
            }
        }

        FloatingText.Spawn($"-{total} ¤", _player.transform.position, new Color(0.9f, 0.3f, 0.3f));
        RefreshBuyPrice();
        Debug.Log($"[SHOP] Acheté ×{_quantity} {_selectedEntry.item?.name} pour {total} ¤");
    }

    private void SellSelected()
    {
        if (_selectedItem == null || _player == null) return;

        // ── Vérification quantité disponible ─────────────────
        int available = GetAvailableQuantity(_selectedItem);
        if (available <= 0) return;
        _quantity = Mathf.Min(_quantity, available);

        int unitPrice = GetSellPrice(_selectedItem);
        int total     = unitPrice * _quantity;

        // ── Retrait du stock ──────────────────────────────────
        bool removed = false;

        if (_selectedItem.ResourceInstance != null)
        {
            removed = _selectedItem.ResourceInstance.Remove(_quantity);
            // Si le stack est vide, retirer l'item de l'inventaire
            if (_selectedItem.ResourceInstance.IsEmpty)
                InventorySystem.Instance?.RemoveItem(_selectedItem);
        }
        else if (_selectedItem.ConsumableInstance != null)
        {
            removed = _selectedItem.ConsumableInstance.Remove(_quantity);
            if (_selectedItem.ConsumableInstance.IsEmpty)
                InventorySystem.Instance?.RemoveItem(_selectedItem);
        }
        else
        {
            // Équipement — vend 1 seul
            removed = InventorySystem.Instance?.RemoveItem(_selectedItem) ?? false;
        }

        if (!removed) return;

        AerisSystem.Instance?.Add(total);
        FloatingText.Spawn($"+{total} ¤", _player.transform.position, new Color(0.4f, 0.9f, 0.4f));
        _player.OnItemSold(_selectedItem.Name, total);

        Debug.Log($"[SHOP] Vendu ×{_quantity} {_selectedItem.Name} pour {total} ¤");

        // Refresh inventaire immédiatement
        InventorySystem.Instance?.OnInventoryChanged?.Invoke();
        InventoryUI.Instance?.RefreshGrid();

        ClearDetail();
        RefreshGrid();
    }

    /// <summary>Quantité réellement disponible pour la vente.</summary>
    private int GetAvailableQuantity(InventoryItem item)
    {
        if (item == null) return 0;
        if (item.ResourceInstance   != null) return item.ResourceInstance.quantity;
        if (item.ConsumableInstance != null) return item.ConsumableInstance.quantity;
        return 1; // équipements — toujours 1
    }

    // =========================================================
    // RÉSOLUTION ITEM
    // =========================================================

    private InventoryItem ResolveItemFromEntry(ShopEntry entry)
    {
        if (entry?.item == null) return null;

        switch (entry.item)
        {
            case WeaponData wd:     return new InventoryItem(wd.CreateDropInstance());
            case ArmorData ad:      return new InventoryItem(ad.CreateDropInstance());
            case HelmetData hd:     return new InventoryItem(hd.CreateInstance());
            case GlovesData gd:     return new InventoryItem(gd.CreateInstance());
            case BootsData bd:      return new InventoryItem(bd.CreateInstance());
            case JewelryData jd:    return new InventoryItem(jd.CreateInstance());
            case ConsumableData cd: return new InventoryItem(cd.CreateInstance());
            case ResourceData rd:   return new InventoryItem(rd.CreateInstance());
            case SkillData:             return null;
            case PermanentSkillData:    return null; // géré dans BuySelected
            default:
                Debug.LogWarning($"[SHOP] Type SO non géré : {entry.item.GetType().Name}");
                return null;
        }
    }

    // =========================================================
    // HELPERS — icône / nom / desc depuis ShopEntry
    // =========================================================

    private Sprite GetEntryIcon(ShopEntry entry)
    {
        switch (entry.item)
        {
            case WeaponData wd:     return wd.icon;
            case ArmorData ad:      return ad.icon;
            case HelmetData hd:     return hd.icon;
            case GlovesData gd:     return gd.icon;
            case BootsData bd:      return bd.icon;
            case JewelryData jd:    return jd.icon;
            case ConsumableData cd: return cd.icon;
            case ResourceData rd:   return rd.icon;
            case SkillData sd:      return sd.icon;
            case PermanentSkillData pd: return pd.icon;
            default:                return null;
        }
    }

    private string GetEntryName(ShopEntry entry)
    {
        switch (entry.item)
        {
            case ItemData id:           return id.displayName.Get(LocalizationManager.CurrentLanguage);
            case SkillData sd:          return sd.skillName.Get(LocalizationManager.CurrentLanguage);
            case PermanentSkillData pd: return pd.skillName.Get(LocalizationManager.CurrentLanguage);
            default:                    return entry.item?.name ?? "???";
        }
    }

    private string GetEntryDesc(ShopEntry entry)
    {
        switch (entry.item)
        {
            case ItemData id:           return id.description.Get(LocalizationManager.CurrentLanguage);
            case SkillData sd:          return sd.description.Get(LocalizationManager.CurrentLanguage);
            case PermanentSkillData pd: return !pd.description.IsEmpty ? pd.description.Get(LocalizationManager.CurrentLanguage) : pd.GetBonusSummary();
            default:                    return "";
        }
    }

    // =========================================================
    // PRIX DE VENTE
    // =========================================================

    /// <summary>
    /// Prix de revente d'une pièce d'équipement : vendorPrice (référence saisie sur
    /// le SO) × la stat qui porte sa "puissance" (dégâts pour une arme, somme des
    /// défenses pour le reste). Si la stat vaut 0 (ex: casque sans défense propre,
    /// juste des bonus via config), retombe sur vendorPrice seul plutôt que sur 0 —
    /// vendorPrice = 0 reste le seul moyen de rendre un item non vendable (GDD §5.1).
    /// </summary>
    private int EquipSellPrice(int vendorPrice, float powerStat)
        => powerStat > 0f ? Mathf.RoundToInt(vendorPrice * powerStat) : vendorPrice;

    private int GetSellPrice(InventoryItem item)
    {
        if (item == null) return 0;

        int basePrice = 0;
        if      (item.ResourceInstance   != null) basePrice = item.ResourceInstance.SellPrice;
        else if (item.ConsumableInstance != null) basePrice = Mathf.RoundToInt((item.ConsumableInstance.data?.healHP ?? 0) / 10f) + 5;
        else if (item.WeaponInstance     != null) basePrice = EquipSellPrice(item.WeaponInstance.data?.vendorPrice ?? 0, item.WeaponInstance.FinalDamageMax);
        else if (item.ArmorInstance      != null) basePrice = EquipSellPrice(item.ArmorInstance.data?.vendorPrice ?? 0,
                                                        item.ArmorInstance.FinalMeleeDefense + item.ArmorInstance.FinalRangedDefense + item.ArmorInstance.FinalMagicDefense);
        else if (item.HelmetInstance     != null) basePrice = EquipSellPrice(item.HelmetInstance.data?.vendorPrice ?? 0,
                                                        item.HelmetInstance.MeleeDefense + item.HelmetInstance.RangedDefense + item.HelmetInstance.MagicDefense);
        else if (item.GlovesInstance     != null) basePrice = EquipSellPrice(item.GlovesInstance.data?.vendorPrice ?? 0,
                                                        item.GlovesInstance.MeleeDefense + item.GlovesInstance.RangedDefense + item.GlovesInstance.MagicDefense);
        else if (item.BootsInstance      != null) basePrice = EquipSellPrice(item.BootsInstance.data?.vendorPrice ?? 0,
                                                        item.BootsInstance.MeleeDefense + item.BootsInstance.RangedDefense + item.BootsInstance.MagicDefense);
        else if (item.JewelryInstance    != null) basePrice = EquipSellPrice(item.JewelryInstance.data?.vendorPrice ?? 0,
                                                        item.JewelryInstance.MeleeDefense + item.JewelryInstance.RangedDefense + item.JewelryInstance.MagicDefense);
        else if (item.SpiritInstance         != null) basePrice = item.SpiritInstance.data?.vendorPrice ?? 0;
        else if (item.CosmeticInstanceHead   != null) basePrice = item.CosmeticInstanceHead.data?.vendorPrice ?? 0;
        else if (item.CosmeticInstanceBody   != null) basePrice = item.CosmeticInstanceBody.data?.vendorPrice ?? 0;
        else if (item.CardInstance           != null) basePrice = item.CardInstance.data?.vendorPrice ?? 0;
        else if (item.RuneInstance       != null) basePrice = item.RuneInstance.runeLevel * 2;
        else if (item.GemInstance        != null) basePrice = item.GemInstance.GemLevel * 5;

        if (basePrice <= 0) return 0;

        int rank = _player != null ? _player.worldReputationRank : 0;
        float mult = rank < SELL_MULTIPLIERS.Length ? SELL_MULTIPLIERS[rank] : SELL_MULTIPLIERS[SELL_MULTIPLIERS.Length - 1];
        return Mathf.RoundToInt(basePrice * mult);
    }

    // =========================================================
    // UTILITAIRES UI
    // =========================================================

    private void RefreshAeris(int amount)
    {
        if (aerisText != null) aerisText.text = $"{amount:N0} ¤";
        if (_activeTab == ShopTab.Buy && _selectedEntry != null) RefreshBuyPrice();
    }

    private void SetDetailIcon(Sprite sprite)
    {
        if (detailIcon == null) return;
        detailIcon.sprite  = sprite;
        detailIcon.enabled = sprite != null;
    }

    private string GetItemDescription(InventoryItem item)
    {
        if (item == null) return "";
        Language lang = LocalizationManager.CurrentLanguage;
        if (item.WeaponInstance?.data     != null) return item.WeaponInstance.data.description.Get(lang);
        if (item.ArmorInstance?.data      != null) return item.ArmorInstance.data.description.Get(lang);
        if (item.HelmetInstance?.data     != null) return item.HelmetInstance.data.description.Get(lang);
        if (item.GlovesInstance?.data     != null) return item.GlovesInstance.data.description.Get(lang);
        if (item.BootsInstance?.data      != null) return item.BootsInstance.data.description.Get(lang);
        if (item.ConsumableInstance?.data != null) return item.ConsumableInstance.data.description.Get(lang);
        if (item.ResourceInstance?.data   != null) return item.ResourceInstance.data.description.Get(lang);
        return "";
    }

    private void ClearDetail()
    {
        if (detailPanel != null) detailPanel.SetActive(false);
        _selectedEntry = null;
        _selectedItem  = null;
        _quantity      = 1;
    }

    private void HideReputationWarning()
    {
        if (reputationWarning != null) reputationWarning.gameObject.SetActive(false);
    }

    private void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null) label.text = value ?? "";
    }
}