using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// =============================================================
// INVENTORYITEMCELL — Cellule d'inventaire avec drag & drop
// Path : Assets/Scripts/UI/InventoryItemCell.cs
// AetherTree GDD v30
// =============================================================
public class InventoryItemCell : MonoBehaviour,
    IPointerClickHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [HideInInspector] public Image           iconImage;
    [HideInInspector] public Image           bgImage;
    [HideInInspector] public TextMeshProUGUI rarityText;
    [HideInInspector] public TextMeshProUGUI nameText;
    [HideInInspector] public Image           cdOverlay;
    [HideInInspector] public TextMeshProUGUI cdText;

    public InventoryItem Item { get; private set; }

    private InventoryUI   _ui;
    private RectTransform _rect;

    private static readonly Color FilledColor = Color.white;

    public void Init(InventoryUI ui)
    {
        _ui     = ui;
        _rect   = GetComponent<RectTransform>();

        // BG = fond coloré (enfant "BG")
        bgImage   = transform.Find("BG")?.GetComponent<Image>()
                 ?? GetComponent<Image>(); // fallback racine
        // Icon = icône de l'item (enfant "Image")
        iconImage = transform.Find("Image")?.GetComponent<Image>()
                 ?? transform.Find("Icon")?.GetComponent<Image>()
                 ?? transform.Find("ItemIcon")?.GetComponent<Image>();
        // Fallback : premier Image enfant qui n'est pas BG ni la racine
        if (iconImage == null)
        {
            foreach (var img in GetComponentsInChildren<Image>())
            {
                if (img != bgImage && img.gameObject != gameObject)
                { iconImage = img; break; }
            }
        }
        nameText = transform.Find("Count")?.GetComponent<TextMeshProUGUI>()
                ?? transform.Find("Quantity")?.GetComponent<TextMeshProUGUI>()
                ?? GetComponentInChildren<TextMeshProUGUI>();

        // Cooldown — même enfants nommés que ConsoBarUI/PassifBarUI (CDOverlay/CD), copie
        // conforme du cooldown partagé par ConsumableData (demande Florian : un consommable
        // en CD dans la Conso Bar doit aussi s'afficher en CD dans l'inventaire).
        cdOverlay = transform.Find("CDOverlay")?.GetComponent<Image>();
        cdText    = transform.Find("CD")?.GetComponent<TextMeshProUGUI>();
        if (cdOverlay != null)
        {
            cdOverlay.type       = Image.Type.Filled;
            cdOverlay.fillMethod = Image.FillMethod.Radial360;
            cdOverlay.fillAmount = 0f;
            cdOverlay.gameObject.SetActive(false);
        }
    }

    /// <summary>Poll chaque frame, même pattern que ConsoSlotBarUI.Update() — le cooldown est
    /// partagé par ConsumableData (pas par slot/cellule), donc une cellule inventaire affiche le
    /// même état qu'un slot de la Conso Bar pour le même item, sans event ni synchro explicite.</summary>
    private void Update()
    {
        if (cdOverlay == null) return;

        ConsumableData data = Item?.ConsumableInstance?.data;
        if (data == null || ConsoBarUI.Instance == null)
        {
            if (cdOverlay.gameObject.activeSelf) SetCooldown(0f, 0f);
            return;
        }

        float remaining = ConsoBarUI.Instance.GetCooldownRemaining(data);
        float total      = ConsoBarUI.Instance.GetCooldownDuration(data);
        SetCooldown(remaining, total);
    }

    public void SetCooldown(float remaining, float total)
    {
        bool onCD = remaining > 0f && total > 0f;
        if (cdOverlay != null) { cdOverlay.gameObject.SetActive(onCD); cdOverlay.fillAmount = onCD ? remaining / total : 0f; }
        if (cdText    != null) { cdText.gameObject.SetActive(onCD);    cdText.text = onCD ? Mathf.CeilToInt(remaining).ToString() : ""; }
    }

    public void SetItem(InventoryItem item)
    {
        Item = item;

        // Fond BG — slot vide légèrement différent du slot occupé
        if (bgImage != null)
            bgImage.color = item != null
                ? new Color(0.25f, 0.25f, 0.30f, 1f)  // occupé
                : new Color(0.18f, 0.18f, 0.20f, 1f);  // vide

        // Icône
        if (iconImage != null)
        {
            iconImage.sprite  = item?.Icon;
            iconImage.color   = item?.Icon != null ? FilledColor : new Color(0, 0, 0, 0);
            iconImage.enabled = item?.Icon != null;
        }

        // Count — quantité uniquement pour stackables, rien pour équipements
        if (nameText != null)
            nameText.text = item?.CellLabel ?? "";

        // Tooltip
        GetComponent<TooltipTrigger>()?.SetItem(item);
    }

    // ── Clic / Double clic ───────────────────────────────────

    private float _lastClickTime = 0f;
    private const float DOUBLE_CLICK_DELAY = 0.3f;

    public void OnPointerClick(PointerEventData e)
    {
        if (Item == null) return;
        if (e.button != PointerEventData.InputButton.Left) return;

        float now = Time.unscaledTime;
        if (now - _lastClickTime < DOUBLE_CLICK_DELAY)
        {
            // Double clic → équipe/déséquipe
            _ui?.OnCellDoubleClicked(this);
            _lastClickTime = 0f;
        }
        else
        {
            _lastClickTime = now;
        }
    }

    // ── Drag ─────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData e)
    {
        if (Item == null) return;
        InventoryUI.BeginDrag(this);
        DragGhost.Begin(iconImage?.sprite, new Color(1f, 1f, 1f, 0.7f), _rect.sizeDelta, e);
    }

    public void OnDrag(PointerEventData e)
    {
        DragGhost.Move(e);
    }

    public void OnEndDrag(PointerEventData e)
    {
        InventoryUI.EndDrag();
        DragGhost.End();
    }

    // ── Drop ─────────────────────────────────────────────────

    public void OnDrop(PointerEventData e)
    {
        var draggedItem = InventoryUI.DraggedItem;
        if (draggedItem == null) return;

        if (InventoryUI.DraggedCell != null && InventoryUI.DraggedCell != this)
        {
            // Swap entre deux cellules inventaire
            var tmp = InventoryUI.DraggedCell.Item;
            InventoryUI.DraggedCell.SetItem(this.Item);
            this.SetItem(tmp);
        }
        else if (InventoryUI.DraggedCell == null)
        {
            var returnCallback = InventoryUI.ConsumeReturnToSource();
            if (returnCallback != null)
            {
                // Drop depuis un slot de mise en scène externe (Fusion/Rareté/Forge...) —
                // le panel appelant sait lui-même remettre l'item en inventaire et nettoyer
                // sa propre référence (_staged, _slot1Item...), voir BeginDragFromExternalSlot.
                returnCallback.Invoke();
                return;
            }

            // Drop depuis slot équipé → déséquipe vers l'inventaire
            var player = UnityEngine.Object.FindObjectOfType<Player>();
            if (player == null) return;
            InventorySystem.Instance?.UnequipToInventory(draggedItem.Slot, player);
            GameEventBus.Publish(new StatsChangedEvent { player = player });
            CharacterPanelUI.Instance?.Refresh();
            InventoryUI.Instance?.RefreshGrid();
        }
    }
}