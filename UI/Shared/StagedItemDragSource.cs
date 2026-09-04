using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// =============================================================
// STAGEDITEMDRAGSOURCE.CS — Re-glisser un item hors d'un slot de mise en
// scène (Fusion/Rareté/Forge...) pour le remettre dans l'inventaire.
// Path : Assets/Scripts/UI/Shared/StagedItemDragSource.cs
//
// Posé sur le même GameObject que l'Image d'un slot (FusionUI.slot1Icon/
// slot2Icon, RarityUI.slotIcon, ForgeUI.slotIcon...). Symétrique du drag
// entrant (InventoryItemCell → slot) — démarre un drag via
// InventoryUI.BeginDragFromExternalSlot(), qui route un futur drop sur une
// InventoryItemCell vers le callback fourni au lieu du "déséquiper" par
// défaut. Le panel appelant reste seul responsable de ce que "remis en
// inventaire" signifie pour lui (AddItem + nettoyage de sa propre
// référence _staged/_slotN — voir Init()).
//
// Init() DOIT être appelé une fois par le panel propriétaire (Awake/Start)
// avant tout drag — sans ça, OnBeginDrag ne fait rien (getItem/onReturned
// non branchés).
// =============================================================
public class StagedItemDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private System.Func<InventoryItem> _getItem;
    private System.Action              _onReturnedToInventory;
    private RectTransform              _rect;
    private Canvas                     _canvas;
    private GameObject                 _dragGhost;

    public void Init(System.Func<InventoryItem> getItem, System.Action onReturnedToInventory)
    {
        _getItem               = getItem;
        _onReturnedToInventory = onReturnedToInventory;
        _rect   = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
    }

    public void OnBeginDrag(PointerEventData e)
    {
        var item = _getItem?.Invoke();
        if (item == null || _canvas == null) return;

        InventoryUI.BeginDragFromExternalSlot(item, _onReturnedToInventory);

        _dragGhost = new GameObject("DragGhost");
        _dragGhost.transform.SetParent(_canvas.transform, false);
        _dragGhost.transform.SetAsLastSibling();

        var ghostRect       = _dragGhost.AddComponent<RectTransform>();
        ghostRect.sizeDelta = _rect.sizeDelta;

        var ghostImg           = _dragGhost.AddComponent<Image>();
        ghostImg.sprite        = item.Icon;
        ghostImg.color         = new Color(1f, 1f, 1f, 0.7f);
        ghostImg.raycastTarget = false;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.transform as RectTransform,
            e.position, _canvas.worldCamera, out Vector2 pos);
        ghostRect.localPosition = pos;
    }

    public void OnDrag(PointerEventData e)
    {
        if (_dragGhost == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.transform as RectTransform,
            e.position, _canvas.worldCamera, out Vector2 pos);
        (_dragGhost.transform as RectTransform).localPosition = pos;
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (_dragGhost != null) { Destroy(_dragGhost); _dragGhost = null; }
        InventoryUI.EndDrag();
    }
}
