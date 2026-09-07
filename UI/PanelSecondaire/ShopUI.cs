using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// =============================================================
// SHOPUI — Interface de shop PNJ
// Path : Assets/Scripts/UI/PanelSecondaire/ShopUI.cs
// AetherTree GDD v3.6 — §13.2 / §19 / §14
//
// Achat uniquement dans la grille — pas d'onglet Vente. Vendre = glisser un
// item depuis l'inventaire vers ShopSellDropSlot (même schéma que
// RarityDropSlot/ForgeDropSlot). Clic sur une cellule = sélection (liseré),
// aucune action tant que le joueur ne clique pas buyButton (ou double-clic
// direct sur la cellule, raccourci qui saute la sélection). Ouvre ensuite
// TransactionConfirmUI — étape Quantité puis étape Confirmation, pas de
// canalisation (réservée à Craft/Upgrade/Pari).
// =============================================================

public class ShopUI : MonoBehaviour
{
    public static ShopUI Instance { get; private set; }

    [Header("Header")]
    public TextMeshProUGUI shopTitleText;
    public TextMeshProUGUI aerisText;
    public Button          closeButton;

    [Header("Grille (achat)")]
    public Transform  shopGridContent;
    public GameObject shopCellPrefab;
    public Button      buyButton;

    private const float DOUBLE_CLICK_DELAY = 0.3f; // même valeur que InventoryItemCell

    private PNJData  _pnjData;
    private Player   _player;

    private ShopEntry  _selectedEntry;
    private GameObject _selectedCell;

    private readonly List<GameObject> _cells = new List<GameObject>();

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
        buyButton?.onClick.AddListener(() =>
        {
            if (_selectedEntry != null) OpenQuantityStep(_selectedEntry);
        });

        if (AerisSystem.Instance != null)
            AerisSystem.Instance.OnAerisChanged += RefreshAeris;
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
        _pnjData = pnjData;
        _player  = player;

        gameObject.SetActive(true);
        if (shopTitleText != null) shopTitleText.text = pnjData.pnjName;

        RefreshAeris(AerisSystem.Instance?.Aeris ?? 0);
        RefreshGrid();

        // Ouvre l'inventaire du joueur automatiquement
        InventoryUI.Instance?.Open();
    }

    public void CloseShop()
    {
        gameObject.SetActive(false);
        InventoryUI.Instance?.Close();
        TransactionConfirmUI.Instance?.Close();
        _pnjData = null;
        _player  = null;
        ClearGrid();
    }

    // =========================================================
    // GRILLE (achat uniquement)
    // =========================================================

    private void RefreshGrid()
    {
        ClearGrid();
        _selectedEntry = null;
        _selectedCell  = null;
        if (buyButton != null) buyButton.interactable = false;
        if (_pnjData == null || shopCellPrefab == null || shopGridContent == null) return;

        foreach (ShopEntry entry in _pnjData.shopItems)
        {
            if (entry?.item == null) continue;

            bool reputationLocked = _player.worldReputationRank < entry.requiredWorldReputationRank;
            bool exhausted        = ShopStockRegistry.Instance != null
                                 && ShopStockRegistry.Instance.IsExhausted(_pnjData.pnjName, entry);
            bool locked           = reputationLocked || exhausted;

            Sprite icon = GetEntryIcon(entry);

            // Aperçu pour le tooltip au survol (TooltipTrigger) — instance jetable pour
            // les items, SkillData/PermanentSkillData/PassiveSkillData directement.
            InventoryItem      previewItem      = ResolveItemFromEntry(entry);
            SkillData          previewSkill     = entry.item as SkillData;
            PermanentSkillData previewPermanent = entry.item as PermanentSkillData;
            PassiveSkillData   previewPassive   = entry.item as PassiveSkillData;

            var capturedEntry = entry;

            GameObject cell = SpawnCell(
                icon                 : icon,
                price                : entry.aerisCost,
                locked               : locked,
                exhausted            : exhausted,
                priceLabel           : exhausted ? "Acheté" : null,
                tooltipItem          : previewItem,
                tooltipSkill         : previewSkill,
                tooltipPermanentSkill: previewPermanent,
                tooltipPassiveSkill  : previewPassive
            );

            if (locked) continue;

            var btn = cell.GetComponent<Button>() ?? cell.GetComponentInChildren<Button>();
            if (btn == null) continue;

            // Simple clic = sélection seulement (idempotent, aucune action irréversible) —
            // un double-clic peut donc être détecté nativement sans risque de double
            // déclenchement : le 1er clic sélectionne, le 2e (dans le délai) enchaîne direct
            // sur la popup Quantité, sans passer par buyButton.
            var capturedCell = cell;
            float lastClickTime = -10f;
            btn.onClick.AddListener(() =>
            {
                bool isDoubleClick = Time.unscaledTime - lastClickTime < DOUBLE_CLICK_DELAY;
                lastClickTime = Time.unscaledTime;

                Debug.Log($"[SHOP-DEBUG] Clic reçu sur '{capturedCell.name}' (doubleClick={isDoubleClick}).");
                SelectEntry(capturedEntry, capturedCell);
                if (isDoubleClick) OpenQuantityStep(capturedEntry);
            });
        }
    }

    /// <summary>Sélectionne une cellule (liseré) — aucune action tant que buyButton
    /// n'est pas cliqué (ou double-clic direct sur la cellule).</summary>
    private void SelectEntry(ShopEntry entry, GameObject cell)
    {
        if (_selectedCell != null) SetCellSelected(_selectedCell, false);
        _selectedEntry = entry;
        _selectedCell  = cell;
        SetCellSelected(cell, true);
        if (buyButton != null) buyButton.interactable = true;
    }

    private void SetCellSelected(GameObject cell, bool selected)
    {
        var outline = cell.transform.Find("SelectedOutline");
        if (outline != null) outline.gameObject.SetActive(selected);
        else Debug.LogWarning($"[SHOP-DEBUG] 'SelectedOutline' introuvable sur la cellule '{cell.name}'.");
    }

    private void OpenQuantityStep(ShopEntry entry)
    {
        if (entry == null) return;

        int maxQty = !entry.isUnlimitedStock
            ? (ShopStockRegistry.Instance?.GetRemainingStock(_pnjData.pnjName, entry) ?? entry.stockCount)
            : 99;
        if (maxQty <= 0) return;

        TransactionConfirmUI.Instance?.OpenBuyFlow(
            itemName : GetEntryName(entry),
            unitPrice: entry.aerisCost,
            maxQty   : maxQty,
            onConfirm: quantity => BuyEntry(entry, quantity)
        );
    }

    private GameObject SpawnCell(Sprite icon, int price, bool locked, bool exhausted = false, string priceLabel = null,
        InventoryItem tooltipItem = null, SkillData tooltipSkill = null, PermanentSkillData tooltipPermanentSkill = null,
        PassiveSkillData tooltipPassiveSkill = null)
    {
        var go = Instantiate(shopCellPrefab, shopGridContent);
        _cells.Add(go);

        var trigger = go.GetComponent<TooltipTrigger>();
        if      (tooltipItem           != null) trigger?.SetItem(tooltipItem);
        else if (tooltipSkill          != null) trigger?.SetSkill(tooltipSkill);
        else if (tooltipPermanentSkill != null) trigger?.SetPermanentSkill(tooltipPermanentSkill);
        else if (tooltipPassiveSkill   != null) trigger?.SetPassiveSkill(tooltipPassiveSkill);

        // Icône sur l'enfant "Icon" — pas la racine (qui porte le Button/le fond de cellule).
        var iconImg = go.transform.Find("Icon")?.GetComponent<Image>() ?? go.GetComponent<Image>();
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

        // Cherche le Button sur la racine OU dans les enfants — interactable/listener
        // gérés par l'appelant (RefreshGrid), pas ici.
        var btn = go.GetComponent<Button>() ?? go.GetComponentInChildren<Button>();
        if (btn != null)
        {
            btn.interactable = !locked;
            btn.onClick.RemoveAllListeners();
        }

        var selectedOutline = go.transform.Find("SelectedOutline");
        if (selectedOutline != null) selectedOutline.gameObject.SetActive(false);

        return go;
    }

    private void ClearGrid()
    {
        foreach (var go in _cells) if (go != null) Destroy(go);
        _cells.Clear();
    }

    // =========================================================
    // ACHAT
    // =========================================================

    private void BuyEntry(ShopEntry entry, int quantity)
    {
        if (entry == null || _player == null || quantity <= 0) return;

        int total = entry.aerisCost * quantity;
        if (!(AerisSystem.Instance?.Spend(total) ?? false))
        {
            Debug.Log("[SHOP] Aeris insuffisants.");
            return;
        }

        if (entry.item is SkillData skill)
        {
            _player.UnlockSkill(skill);
            FloatingText.Spawn($"Skill débloqué !", _player.transform.position, new Color(0.6f, 0.4f, 1f));
        }
        else if (entry.item is PermanentSkillData permanent)
        {
            _player.UnlockPermanent(permanent);
            FloatingText.Spawn($"Passif débloqué !", _player.transform.position, new Color(0.6f, 0.4f, 1f));
        }
        else if (entry.item is PassiveSkillData passive)
        {
            _player.UnlockPassive(passive);
            FloatingText.Spawn($"Passif débloqué !", _player.transform.position, new Color(0.6f, 0.4f, 1f));
        }
        else
        {
            for (int i = 0; i < quantity; i++)
            {
                InventoryItem item = ResolveItemFromEntry(entry);
                if (item != null) InventorySystem.Instance?.AddItem(item);
            }
        }

        if (!entry.isUnlimitedStock)
        {
            ShopStockRegistry.Instance?.RecordPurchase(_pnjData.pnjName, entry, quantity);
            entry.stockCount -= quantity;
        }

        FloatingText.Spawn($"-{total} ¤", _player.transform.position, new Color(0.9f, 0.3f, 0.3f));
        Debug.Log($"[SHOP] Acheté ×{quantity} {entry.item?.name} pour {total} ¤");
        RefreshGrid();
    }

    // =========================================================
    // VENTE — drop depuis l'inventaire (voir ShopSellDropSlot)
    // =========================================================

    /// <summary>Appelé par ShopSellDropSlot.OnDrop() — ouvre le flux Quantité → Confirmation,
    /// quantité bornée à celle possédée.</summary>
    public void OnSellDropped(InventoryItem item)
    {
        if (item == null || _player == null) return;

        int sellPrice = GetSellPrice(item);
        if (sellPrice <= 0)
        {
            Debug.Log("[SHOP] Cet objet ne peut pas être vendu.");
            return;
        }

        int maxQty = GetAvailableQuantity(item);
        if (maxQty <= 0) return;

        TransactionConfirmUI.Instance?.OpenSellFlow(
            itemName : item.DisplayNameRich,
            unitPrice: sellPrice,
            maxQty   : maxQty,
            onConfirm: quantity => SellItem(item, quantity)
        );
    }

    private void SellItem(InventoryItem item, int quantity)
    {
        if (item == null || _player == null || quantity <= 0) return;

        int available = GetAvailableQuantity(item);
        if (available <= 0) return;
        quantity = Mathf.Min(quantity, available);

        int unitPrice = GetSellPrice(item);
        int total     = unitPrice * quantity;

        // ── Retrait du stock ──────────────────────────────────
        bool removed = false;

        if (item.ResourceInstance != null)
        {
            removed = item.ResourceInstance.Remove(quantity);
            if (item.ResourceInstance.IsEmpty)
                InventorySystem.Instance?.RemoveItem(item);
        }
        else if (item.ConsumableInstance != null)
        {
            removed = item.ConsumableInstance.Remove(quantity);
            if (item.ConsumableInstance.IsEmpty)
                InventorySystem.Instance?.RemoveItem(item);
        }
        else
        {
            // Équipement — vend 1 seul
            removed = InventorySystem.Instance?.RemoveItem(item) ?? false;
        }

        if (!removed) return;

        AerisSystem.Instance?.Add(total);
        FloatingText.Spawn($"+{total} ¤", _player.transform.position, new Color(0.4f, 0.9f, 0.4f));
        _player.OnItemSold(item.Name, total);

        Debug.Log($"[SHOP] Vendu ×{quantity} {item.Name} pour {total} ¤");

        InventorySystem.Instance?.OnInventoryChanged?.Invoke();
        InventoryUI.Instance?.RefreshGrid();
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
            case SpiritData sd:     return new InventoryItem(new SpiritInstance(sd));
            case ConsumableData cd: return new InventoryItem(cd.CreateInstance());
            case ResourceData rd:   return new InventoryItem(rd.CreateInstance());
            case TalismanData td:   return new InventoryItem(td.CreateInstance());
            case SkillData:             return null;
            case PermanentSkillData:    return null; // géré dans BuyEntry
            case PassiveSkillData:      return null; // géré dans BuyEntry
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
            case SpiritData sprd:   return sprd.icon;
            case ConsumableData cd: return cd.icon;
            case ResourceData rd:   return rd.icon;
            case SkillData sd:      return sd.icon;
            case PermanentSkillData pd: return pd.icon;
            case PassiveSkillData psd:  return psd.icon;
            case TalismanData td:   return td.icon;
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
            case PassiveSkillData psd:  return psd.skillName.Get(LocalizationManager.CurrentLanguage);
            default:                    return entry.item?.name ?? "???";
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

    /// <summary>Prix de revente Arme/Armure : vendorPrice × (1 + rarityRank × 10%) —
    /// même barème que RarityBonus (GDD §5.13, -20% à +70%). Pourcentage plutôt qu'un
    /// montant fixe : s'adapte automatiquement au vendorPrice de chaque item, rien à
    /// resaisir. Ni la stat finale (Final*), ni le roll, ni l'upgrade n'entrent en
    /// compte : seule la rareté fait varier le prix entre deux exemplaires de la même
    /// arme/armure.</summary>
    private int RarityAdjustedSellPrice(int vendorPrice, int rarityRank)
        => Mathf.RoundToInt(vendorPrice * (1f + rarityRank * 0.10f));

    private static readonly float[] SELL_MULTIPLIERS = { 1.0f, 1.05f, 1.10f, 1.20f, 1.35f, 1.50f };

    private int GetSellPrice(InventoryItem item)
    {
        if (item == null) return 0;

        int basePrice = 0;
        if      (item.ResourceInstance   != null) basePrice = item.ResourceInstance.SellPrice;
        else if (item.ConsumableInstance != null) basePrice = Mathf.RoundToInt((item.ConsumableInstance.data?.healHP ?? 0) / 10f) + 5;
        else if (item.WeaponInstance     != null) basePrice = RarityAdjustedSellPrice(item.WeaponInstance.data?.vendorPrice ?? 0, item.WeaponInstance.rarityRank);
        else if (item.ArmorInstance      != null) basePrice = RarityAdjustedSellPrice(item.ArmorInstance.data?.vendorPrice ?? 0, item.ArmorInstance.rarityRank);
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
        // Invendable une fois activé (chrono démarré) — même mécanisme que vendorPrice=0
        // ailleurs (GDD §5.1), calculé dynamiquement ici plutôt que fixé sur le SO.
        else if (item.TalismanInstance       != null) basePrice = item.TalismanInstance.IsActivated
                                                        ? 0 : (item.TalismanInstance.data?.vendorPrice ?? 0);
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
    }
}
