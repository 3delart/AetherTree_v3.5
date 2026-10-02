using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// =============================================================
// EQUIPMENTSLOT — Drag & Drop sur les slots du CharacterPanel
// Path : Assets/Scripts/UI/EquipmentSlot.cs
// AetherTree GDD v30
//
// Poser sur chaque Image de slot du CharacterPanel.
// Régler slotType dans l'Inspector.
//
// Drag  → déséquipe l'item et initie un drag vers l'inventaire
// Drop  → équipe l'item dragué depuis l'inventaire
// =============================================================
public class EquipmentSlotHandler : MonoBehaviour,
    IPointerClickHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [Tooltip("Slot d'équipement de ce GameObject.")]
    public EquipmentSlot slotType;

    private Player        _player;
    private RectTransform _rect;

    private void Start()
    {
        _player = FindObjectOfType<Player>();
        _rect   = GetComponent<RectTransform>();
    }

    // =========================================================
    // DOUBLE CLIC — déséquipe vers l'inventaire
    // =========================================================

    private float _lastClickTime = 0f;
    private const float DOUBLE_CLICK_DELAY = 0.3f;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        float now = Time.unscaledTime;
        if (now - _lastClickTime < DOUBLE_CLICK_DELAY)
        {
            // Double clic → déséquipe vers l'inventaire
            if (_player != null && InventorySystem.Instance != null)
            {
                InventorySystem.Instance.UnequipToInventory(slotType, _player);
                GameEventBus.Publish(new StatsChangedEvent { player = _player });
                CharacterPanelUI.Instance?.Refresh();
                InventoryUI.Instance?.RefreshGrid();
                Debug.Log($"[SLOT] {slotType} déséquipé (double clic).");
            }
            _lastClickTime = 0f;
        }
        else
        {
            _lastClickTime = now;
        }
    }

    // =========================================================
    // DRAG — depuis le slot équipé vers l'inventaire
    // =========================================================

    public void OnBeginDrag(PointerEventData e)
    {
        if (_player == null) return;

        var item = GetEquippedItem();
        if (item == null) return;

        InventoryUI.BeginDragEquipped(item);
        DragGhost.Begin(item.Icon, new Color(1f, 1f, 1f, 0.7f),
            _rect != null ? _rect.sizeDelta : new Vector2(64, 64), e);
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

    // =========================================================
    // DROP — depuis l'inventaire vers ce slot
    // =========================================================

    public void OnDrop(PointerEventData e)
    {
        var item = InventoryUI.DraggedItem;
        if (item == null || _player == null) return;

        if (item.Slot != slotType)
        {
            Debug.Log($"[SLOT] {item.Name} ({item.Slot}) incompatible avec {slotType}");
            return;
        }

        bool success = InventorySystem.Instance?.EquipItem(item, _player) ?? false;
        if (success)
        {
            GameEventBus.Publish(new StatsChangedEvent { player = _player });
            CharacterPanelUI.Instance?.Refresh();
            InventoryUI.Instance?.RefreshGrid();
            Debug.Log($"[SLOT] {item.Name} équipé sur {slotType}.");
        }
    }

    // =========================================================
    // UTILITAIRE
    // =========================================================

    private InventoryItem GetEquippedItem()
    {
        // Récupère l'instance équipée selon le slot
        object instance = null;
        switch (slotType)
        {
            case EquipmentSlot.Weapon:
                instance = _player.equippedWeaponInstance?.data != null
                    ? _player.equippedWeaponInstance : null; break;
            case EquipmentSlot.Armor:
                instance = _player.equippedArmorInstance?.data != null
                    ? _player.equippedArmorInstance : null; break;
            case EquipmentSlot.Helmet:
                instance = _player.equippedHelmetInstance?.data != null
                    ? _player.equippedHelmetInstance : null; break;
            case EquipmentSlot.Gloves:
                instance = _player.equippedGlovesInstance?.data != null
                    ? _player.equippedGlovesInstance : null; break;
            case EquipmentSlot.Boots:
                instance = _player.equippedBootsInstance?.data != null
                    ? _player.equippedBootsInstance : null; break;
            case EquipmentSlot.Spirit:
                instance = _player.equippedSpiritInstances?.Count > 0
                    ? _player.equippedSpiritInstances[0] : null; break;
            case EquipmentSlot.Talisman:
                instance = _player.equippedTalismanInstance?.data != null
                    ? _player.equippedTalismanInstance : null; break;
            case EquipmentSlot.CosmeticHead:
                instance = _player.equippedCosmeticHeadInstance?.data != null
                    ? _player.equippedCosmeticHeadInstance : null; break;
            case EquipmentSlot.CosmeticBody:
                instance = _player.equippedCosmeticBodyInstance?.data != null
                    ? _player.equippedCosmeticBodyInstance : null; break;
            case EquipmentSlot.Ring:
            case EquipmentSlot.Necklace:
            case EquipmentSlot.Bracelet:
                instance = _player.equippedJewelryInstances?.Find(j =>
                    j != null && (
                        (slotType == EquipmentSlot.Ring      && j.Slot == JewelrySlot.Ring)     ||
                        (slotType == EquipmentSlot.Necklace  && j.Slot == JewelrySlot.Necklace) ||
                        (slotType == EquipmentSlot.Bracelet  && j.Slot == JewelrySlot.Bracelet)
                    )); break;
        }
        if (instance == null) return null;

        // Cherche l'InventoryItem existant dans l'inventaire — jamais de new InventoryItem
        var existing = InventorySystem.Instance?.GetItemByInstance(instance);
        if (existing != null) return existing;

        // Fallback — crée un wrapper temporaire si l'item n'est pas dans l'inventaire
        // (cas d'un item équipé au démarrage sans passer par l'inventaire)
        switch (slotType)
        {
            case EquipmentSlot.Weapon:   return new InventoryItem((WeaponInstance)instance);
            case EquipmentSlot.Armor:    return new InventoryItem((ArmorInstance)instance);
            case EquipmentSlot.Helmet:   return new InventoryItem((HelmetInstance)instance);
            case EquipmentSlot.Gloves:   return new InventoryItem((GlovesInstance)instance);
            case EquipmentSlot.Boots:    return new InventoryItem((BootsInstance)instance);
            case EquipmentSlot.Spirit:   return new InventoryItem((SpiritInstance)instance);
            case EquipmentSlot.Talisman: return new InventoryItem((TalismanInstance)instance);
            case EquipmentSlot.CosmeticHead: return new InventoryItem((CosmeticInstanceHead)instance);
            case EquipmentSlot.CosmeticBody: return new InventoryItem((CosmeticInstanceBody)instance);
            default:                     return new InventoryItem((JewelryInstance)instance);
        }
    }
}
