using UnityEngine;
using UnityEngine.EventSystems;

// =============================================================
// SHOPSELLDROPSLOT.CS — Drop zone de vente du panneau Boutique
// Path : Assets/Scripts/UI/PanelSecondaire/ShopSellDropSlot.cs
//
// Poser sur le GameObject du slot de vente dans ShopUI. Accepte n'importe
// quel InventoryItem vendable (GetSellPrice > 0) — même schéma que
// RarityDropSlot/ForgeDropSlot : ne fait que référencer, jamais d'appel
// InventorySystem.RemoveItem directement, la vente réelle n'a lieu qu'à la
// confirmation dans TransactionConfirmUI (voir ShopUI.OnSellDropped).
// =============================================================

public class ShopSellDropSlot : MonoBehaviour, IDropHandler
{
    public void OnDrop(PointerEventData e)
    {
        var item = InventoryUI.DraggedItem;
        if (item == null) return;

        ShopUI.Instance?.OnSellDropped(item);
    }
}
