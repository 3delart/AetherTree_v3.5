using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// =============================================================
// INVENTORYUI.CS — Panneau inventaire avec 4 onglets
// Path : Assets/Scripts/UI/InventoryUI.cs
// AetherTree GDD v30
//
// Onglets :
//   Equipements  — Weapon, Armor, Helmet, Gloves, Boots,
//                  Ring, Necklace, Bracelet, Spirit
//   Consommables — TODO
//   Ressources   — TODO
//   Cosmetiques  — TODO
//
// Interactions :
//   Clic gauche → équipe l'item (Equipements)
//   Drag & Drop → dépose sur EquipmentDropSlot du CharacterPanel
//
// Setup Unity :
//   Script sur InventoryPanel.
//   Assigner dans l'Inspector tous les champs ci-dessous.
// =============================================================

public class InventoryUI : MonoBehaviour
{
    public static InventoryUI Instance { get; private set; }

    // ── Bouton fermeture ──────────────────────────────────────
    [Header("Bouton fermeture")]
    public Button closeButton;

    // ── Onglets — Boutons ─────────────────────────────────────
    [Header("Onglets — Boutons")]
    public Button tabEquipementsButton;
    public Button tabConsommablesButton;
    public Button tabRessourcesButton;
    public Button tabCosmetiquesButton;

    // ── Onglets — Panels ──────────────────────────────────────
    [Header("Onglets — Panels (GameObjects)")]
    public GameObject panelEquipements;
    public GameObject panelConsommables;
    public GameObject panelRessources;
    public GameObject panelCosmetiques;

    // ── Onglets — Content (Grid) ──────────────────────────────
    [Header("Onglets — Content (Grid Layout Group Transform)")]
    public Transform contentEquipements;
    public Transform contentConsommables;
    public Transform contentRessources;
    public Transform contentCosmetiques;

    // ── Prefab cellule ────────────────────────────────────────
    [Header("Prefab cellule")]
    public GameObject itemCellPrefab;

    // ── Info ──────────────────────────────────────────────────
    [Header("Info")]
    public TextMeshProUGUI itemCountText;

    // ── Couleurs onglets ──────────────────────────────────────
    private static readonly Color TAB_ACTIVE   = new Color(0.35f, 0.22f, 0.65f);
    private static readonly Color TAB_INACTIVE = new Color(0.15f, 0.15f, 0.25f);

    // ── Onglets enum ──────────────────────────────────────────
    public enum InventoryTab { Equipements, Consommables, Ressources, Cosmetiques }

    // ── État ──────────────────────────────────────────────────
    private Player          _player;
    private InventorySystem _inventory;
    private InventoryTab    _activeTab = InventoryTab.Equipements;

    // Cellules par onglet
    private readonly List<InventoryItemCell> _cellsEquip = new List<InventoryItemCell>();
    private readonly List<InventoryItemCell> _cellsConso = new List<InventoryItemCell>();
    private readonly List<InventoryItemCell> _cellsRess  = new List<InventoryItemCell>();
    private readonly List<InventoryItemCell> _cellsCosm  = new List<InventoryItemCell>();

    // Slots "Equipements"
    private static readonly EquipmentSlot[] EQUIP_SLOTS =
    {
        EquipmentSlot.Weapon, EquipmentSlot.Armor,  EquipmentSlot.Helmet,
        EquipmentSlot.Gloves, EquipmentSlot.Boots,
        EquipmentSlot.Ring,   EquipmentSlot.Necklace, EquipmentSlot.Bracelet,
        EquipmentSlot.Spirit, EquipmentSlot.Talisman,
    };

    // ── Drag & Drop état global ───────────────────────────────
    public static InventoryItemCell DraggedCell { get; private set; }
    public static InventoryItem     DraggedItem { get; private set; }

    /// <summary>Callback optionnel armé par BeginDragFromExternalSlot() (Fusion/Rareté/
    /// Forge...) — si non-null, InventoryItemCell.OnDrop() l'appelle à la place du
    /// "déséquiper" par défaut quand un drag sans DraggedCell atterrit sur la grille
    /// inventaire. Lu et consommé UNIQUEMENT via ConsumeReturnToSource() (jamais deux fois),
    /// toujours nettoyé par EndDrag() si jamais consommé (drop hors cible valide).</summary>
    private static System.Action _onReturnToSource;

    /// <summary>Récupère puis efface le callback de retour armé par BeginDragFromExternalSlot
    /// (ou null si le drag en cours vient d'ailleurs — équipement, ou pas de drag actif).</summary>
    public static System.Action ConsumeReturnToSource()
    {
        var callback = _onReturnToSource;
        _onReturnToSource = null;
        return callback;
    }

    // =========================================================
    // INIT
    // =========================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _player    = FindObjectOfType<Player>();
        _inventory = InventorySystem.Instance;

        if (_inventory != null)
        {
            _inventory.OnInventoryChanged += RefreshActiveTab;
        }

        closeButton?.onClick.AddListener(Close);

        tabEquipementsButton? .onClick.AddListener(() => SwitchTab(InventoryTab.Equipements));
        tabConsommablesButton?.onClick.AddListener(() => SwitchTab(InventoryTab.Consommables));
        tabRessourcesButton?  .onClick.AddListener(() => SwitchTab(InventoryTab.Ressources));
        tabCosmetiquesButton? .onClick.AddListener(() => SwitchTab(InventoryTab.Cosmetiques));

        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_inventory != null)
        {
            _inventory.OnInventoryChanged -= RefreshActiveTab;
        }
    }

    // =========================================================
    // SWITCH ONGLET
    // =========================================================

    public void SwitchTab(InventoryTab tab)
    {
        _activeTab = tab;

        if (panelEquipements  != null) panelEquipements .SetActive(tab == InventoryTab.Equipements);
        if (panelConsommables != null) panelConsommables.SetActive(tab == InventoryTab.Consommables);
        if (panelRessources   != null) panelRessources  .SetActive(tab == InventoryTab.Ressources);
        if (panelCosmetiques  != null) panelCosmetiques .SetActive(tab == InventoryTab.Cosmetiques);

        SetTabColor(tabEquipementsButton,  tab == InventoryTab.Equipements);
        SetTabColor(tabConsommablesButton, tab == InventoryTab.Consommables);
        SetTabColor(tabRessourcesButton,   tab == InventoryTab.Ressources);
        SetTabColor(tabCosmetiquesButton,  tab == InventoryTab.Cosmetiques);

        RefreshActiveTab();
    }

    private void SetTabColor(Button btn, bool active)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = active ? TAB_ACTIVE : TAB_INACTIVE;
    }

    // =========================================================
    // REFRESH
    // =========================================================

    public void RefreshGrid() => RefreshActiveTab();

    private void RefreshActiveTab()
    {
        switch (_activeTab)
        {
            case InventoryTab.Equipements:  RefreshEquipements();  break;
            case InventoryTab.Consommables: RefreshConsommables(); break;
            case InventoryTab.Ressources:   RefreshRessources();   break;
            case InventoryTab.Cosmetiques:  RefreshCosmetiques();  break;
        }

        if (itemCountText != null && _inventory != null)
            itemCountText.text = $"{_inventory.Count} / {GetUnlockedSlots()}";
    }

    private void RefreshEquipements()
    {
        if (contentEquipements == null || itemCellPrefab == null) return;
        var items = new List<InventoryItem>();
        if (_inventory != null)
            foreach (var slot in EQUIP_SLOTS)
                foreach (var item in _inventory.GetItems(slot))
                    if (item.ItemCategory == InventoryCategory.Equipement
                        || item.ItemCategory == InventoryCategory.Talisman)  // ← filtre ajouté
                        items.Add(item);
        FillGrid(contentEquipements, _cellsEquip, items);
    }

    private void RefreshConsommables()
    {
        if (contentConsommables == null || _inventory == null) return;
        FillGrid(contentConsommables, _cellsConso, _inventory.GetConsommables());
    }

    private void RefreshRessources()
    {
        if (contentRessources == null || _inventory == null) return;
        FillGrid(contentRessources, _cellsRess, _inventory.GetRessources());
    }

    private void RefreshCosmetiques()
    {
        if (contentCosmetiques == null || _inventory == null) return;
        var items = new List<InventoryItem>();
        items.AddRange(_inventory.GetCosmetiquesHead());
        items.AddRange(_inventory.GetCosmetiquesBody());
        FillGrid(contentCosmetiques, _cellsCosm, items);
    }

    /// <summary>Retourne UnlockedSlots si disponible, sinon MAX_SLOTS.</summary>
    private int GetUnlockedSlots()
    {
        // Tente d'accéder à UnlockedSlots via réflexion pour rester compatible
        // avec l'ancienne version de InventorySystem (sans slots débloqués).
        var prop = typeof(InventorySystem).GetProperty("UnlockedSlots");
        if (prop != null) return (int)prop.GetValue(_inventory);
        return InventorySystem.MAX_SLOTS;
    }

    private void FillGrid(Transform content, List<InventoryItemCell> cells, List<InventoryItem> items)
    {
        if (_inventory == null) return;
        int totalSlots = GetUnlockedSlots();

        // Crée les cellules manquantes jusqu'au nombre de slots débloqués
        while (cells.Count < totalSlots)
        {
            var go   = Instantiate(itemCellPrefab, content);
            var cell = go.GetComponent<InventoryItemCell>();
            if (cell == null) cell = go.AddComponent<InventoryItemCell>();
            cell.Init(this);
            cells.Add(cell);
        }

        // Remplit chaque slot — vide si pas d'item à cet index
        for (int i = 0; i < cells.Count; i++)
        {
            cells[i].gameObject.SetActive(i < totalSlots);

            if (i >= totalSlots) continue;

            if (i < items.Count)
                cells[i].SetItem(items[i]);   // slot occupé
            else
                cells[i].SetItem(null);        // slot vide — grisé, non-cliquable
        }
    }

    // =========================================================
    // ÉQUIPEMENT PAR CLIC
    // =========================================================

    public void OnCellClicked(InventoryItemCell cell) { } // simple clic — pas d'action

    /// <summary>Double clic → équipe l'item (inventaire) ou déséquipe (slot équipé).</summary>
    public void OnCellDoubleClicked(InventoryItemCell cell)
    {
        if (cell?.Item == null) return;
        if (_player == null) _player = UnityEngine.Object.FindObjectOfType<Player>();
        if (_inventory == null) _inventory = InventorySystem.Instance;
        if (_player == null) return;

        // Nom capturé AVANT EquipItem() — OnInventoryChanged reconstruit la grille en
        // synchrone dans EquipItem(), qui remet cell.Item à null (item quitte l'inventaire).
        string itemName = cell.Item.Name;
        bool success = _inventory?.EquipItem(cell.Item, _player) ?? false;
        if (success)
        {
            GameEventBus.Publish(new StatsChangedEvent { player = _player });
            CharacterPanelUI.Instance?.Refresh();
            Debug.Log($"[INVENTORY UI] {itemName} équipé (double clic).");
        }
    }

    // =========================================================
    // DRAG & DROP
    // =========================================================

    public static void BeginDrag(InventoryItemCell cell)
    {
        DraggedCell = cell;
        DraggedItem = cell?.Item;
    }

    /// <summary>Démarre un drag depuis un slot équipé du CharacterPanel.</summary>
    public static void BeginDragEquipped(InventoryItem item)
    {
        DraggedCell       = null;  // pas de cellule source
        DraggedItem       = item;
        _onReturnToSource = null;
    }

    /// <summary>Démarre un drag depuis un slot de mise en scène externe (Fusion/Rareté/
    /// Forge...). `onReturned` est appelé UNE fois si le joueur dépose l'item sur une
    /// cellule d'inventaire (via ConsumeReturnToSource dans InventoryItemCell.OnDrop) — à
    /// charge de ce callback de vraiment remettre l'item dans InventorySystem et de nettoyer
    /// la référence locale du panel appelant (_staged, _slot1Item...).</summary>
    public static void BeginDragFromExternalSlot(InventoryItem item, System.Action onReturned)
    {
        DraggedCell       = null;
        DraggedItem       = item;
        _onReturnToSource = onReturned;
    }

    public static void EndDrag()
    {
        DraggedCell       = null;
        DraggedItem       = null;
        _onReturnToSource = null;
    }

    // =========================================================
    // TOGGLE / OPEN / CLOSE
    // =========================================================

    public void Toggle()
    {
        bool next = !gameObject.activeSelf;
        gameObject.SetActive(next);
        if (next) Open();
    }

    public void Open()
    {
        gameObject.SetActive(true);
        // Passe devant les autres panels secondaires (Shop/Forge/Rareté...) dans
        // l'ordre de rendu du Canvas — sinon un panel ouvert après (même sibling
        // parent) peut couvrir visuellement l'inventaire alors qu'il est bien actif.
        transform.SetAsLastSibling();
        SwitchTab(_activeTab);
    }

    public void Close() { gameObject.SetActive(false); }
}
