using UnityEngine;
using UnityEngine.EventSystems;

// =============================================================
// FORGEDROPSLOT.CS — Drop zone du slot Forge
// Path : Assets/Scripts/UI/PanelSecondaire/ForgeDropSlot.cs
//
// Poser sur le GameObject du slot dans le Panel Forge (ForgeUI.cs).
// Accepte un InventoryItem Weapon ou Armor venant UNIQUEMENT de
// l'inventaire — un équipement porté (CharacterPanel) est rejeté (voir
// GetItemByInstance ci-dessous). ForgeUI.SetStaged() retire l'item de
// InventorySystem dès l'acceptation — ce composant n'est qu'un pur
// transport UI, jamais d'appel InventorySystem lui-même.
//
// Fichier séparé de ForgeUI.cs — une classe MonoBehaviour additionnelle
// dans le même fichier compile très bien, mais Unity ne la propose pas
// dans la recherche "Add Component" (seule la classe qui correspond au
// nom du fichier y apparaît). D'où le fichier dédié.
// =============================================================

public class ForgeDropSlot : MonoBehaviour, IDropHandler
{
    public void OnDrop(PointerEventData e)
    {
        if (ForgeUI.Instance != null && ForgeUI.Instance.IsChanneling)
        {
            Debug.Log("[FORGE DROP] Amélioration en cours — impossible de changer d'objet.");
            return;
        }

        var item = InventoryUI.DraggedItem;
        if (item == null) return;

        if (item.WeaponInstance == null && item.ArmorInstance == null)
        {
            Debug.Log("[FORGE DROP] Seules les armes et armures peuvent être améliorées.");
            return;
        }

        // Refuse un item équipé (ou déjà mis en scène ailleurs, ex: staged dans RarityUI) —
        // seul l'inventaire peut alimenter ce slot, même garde que FusionSlotDropTarget.
        object underlyingInstance = item.WeaponInstance != null ? (object)item.WeaponInstance : item.ArmorInstance;
        if (InventorySystem.Instance?.GetItemByInstance(underlyingInstance) == null)
        {
            Debug.Log("[FORGE DROP] Seuls les objets de l'inventaire peuvent être améliorés — déséquipe d'abord.");
            return;
        }

        ForgeUI.Instance?.SetStaged(item);
    }
}
