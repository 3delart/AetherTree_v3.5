using UnityEngine;
using UnityEngine.EventSystems;

// =============================================================
// FUSIONSLOTDROPTARGET.CS — Zone de dépôt drag&drop pour un slot Fusion
// Path : Assets/Scripts/UI/Shared/FusionSlotDropTarget.cs
//
// Composant générique posé sur les 2 GameObjects "Slot 1"/"Slot 2" de FusionUI. Reçoit un
// drag démarré depuis une InventoryItemCell (InventoryUI.DraggedItem déjà alimenté par
// InventoryItemCell.OnBeginDrag — voir UI/Shared/InventoryItemCell.cs). Ne fait aucune
// validation de type ici (Gants vs Bottes, même item aux 2 slots) — c'est FusionUI qui
// valide via le callback, ce composant est un pur transport UI.
// =============================================================
public class FusionSlotDropTarget : MonoBehaviour, IDropHandler
{
    [Tooltip("0 = Slot 1, 1 = Slot 2 — juste pour que FusionUI sache lequel a reçu le drop.")]
    public int slotIndex;

    public System.Action<int, InventoryItem> OnItemDropped;

    public void OnDrop(PointerEventData eventData)
    {
        if (FusionUI.Instance != null && FusionUI.Instance.IsChanneling) return;

        var item = InventoryUI.DraggedItem;
        if (item == null) return;

        OnItemDropped?.Invoke(slotIndex, item);
        InventoryUI.EndDrag();
    }
}
