using UnityEngine;
using UnityEngine.EventSystems;

// =============================================================
// FORGEDROPSLOT.CS — Drop zone du slot Forge
// Path : Assets/Scripts/UI/PanelSecondaire/ForgeDropSlot.cs
//
// Poser sur le GameObject du slot dans le Panel Forge (ForgeUI.cs).
// Accepte un InventoryItem Weapon ou Armor — même schéma que ConsoDropSlot
// (UI/PanelFixe/ConsoBarUI.cs) : ne fait que référencer, jamais d'appel
// InventorySystem.RemoveItem — l'item reste dans l'inventaire/équipé.
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

        ForgeUI.Instance?.SetStaged(item);
    }
}
