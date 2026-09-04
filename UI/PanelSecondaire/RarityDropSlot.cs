using UnityEngine;
using UnityEngine.EventSystems;

// =============================================================
// RARITYDROPSLOT.CS — Drop zone du slot Pari de rareté
// Path : Assets/Scripts/UI/PanelSecondaire/RarityDropSlot.cs
//
// Poser sur le GameObject du slot dans RaretéPanel (RarityUI.cs). Accepte
// un InventoryItem Weapon ou Armor venant UNIQUEMENT de l'inventaire — un
// équipement porté (CharacterPanel) est rejeté (voir GetItemByInstance
// ci-dessous). RarityUI.SetStaged() retire l'item de InventorySystem dès
// l'acceptation — ce composant n'est qu'un pur transport UI, jamais
// d'appel InventorySystem lui-même.
//
// Fichier séparé de RarityUI.cs — une classe MonoBehaviour additionnelle
// dans le même fichier compile très bien, mais Unity ne la propose pas
// dans la recherche "Add Component" (seule la classe qui correspond au
// nom du fichier y apparaît). D'où le fichier dédié (même pattern que
// ForgeDropSlot).
// =============================================================

public class RarityDropSlot : MonoBehaviour, IDropHandler
{
    public void OnDrop(PointerEventData e)
    {
        if (RarityUI.Instance != null && RarityUI.Instance.IsChanneling)
        {
            Debug.Log("[RARITY DROP] Pari en cours — impossible de changer d'objet.");
            return;
        }

        var item = InventoryUI.DraggedItem;
        if (item == null) return;

        if (item.WeaponInstance == null && item.ArmorInstance == null)
        {
            Debug.Log("[RARITY DROP] Seules les armes et armures peuvent être soumises au pari.");
            return;
        }

        // Refuse un item équipé (ou déjà mis en scène ailleurs, ex: staged dans ForgeUI) —
        // seul l'inventaire peut alimenter ce slot, même garde que FusionSlotDropTarget.
        object underlyingInstance = item.WeaponInstance != null ? (object)item.WeaponInstance : item.ArmorInstance;
        if (InventorySystem.Instance?.GetItemByInstance(underlyingInstance) == null)
        {
            Debug.Log("[RARITY DROP] Seuls les objets de l'inventaire peuvent être soumis au pari — déséquipe d'abord.");
            return;
        }

        RarityUI.Instance?.SetStaged(item);
    }
}
