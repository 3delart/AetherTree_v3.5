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

    public void Init(System.Func<InventoryItem> getItem, System.Action onReturnedToInventory)
    {
        _getItem               = getItem;
        _onReturnedToInventory = onReturnedToInventory;
        _rect   = GetComponent<RectTransform>();
    }

    public void OnBeginDrag(PointerEventData e)
    {
        var item = _getItem?.Invoke();
        if (item == null) return;

        InventoryUI.BeginDragFromExternalSlot(item, _onReturnedToInventory);
        DragGhost.Begin(item.Icon, new Color(1f, 1f, 1f, 0.7f), _rect.sizeDelta, e);
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
}
