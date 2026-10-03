using UnityEngine;

// =============================================================
// ITEMDROPFACTORY.CS — Crée un InventoryItem depuis N'IMPORTE quel SO d'item
// Path : Assets/Scripts/Data/Mobs/ItemDropFactory.cs
//
// Extrait de LootTable.CreateInventoryItem (2026-09-24) pour être partagé
// avec ConsumableData.RollChestEntry() (coffre) — même logique de "quel type
// de SO → quelle instance", seule différence : rarityOverride permet à un
// appelant (le coffre, pour que tout ce qui en sort porte SA rareté fixe) de forcer la
// rareté d'une arme/armure au lieu de la rareté aléatoire habituelle
// (WeaponData/ArmorData.RollRarity()). Sans effet sur les autres types
// d'item (Gem/Rune/Consommable/Ressource/etc.) — seuls Armes et Armures
// ont une notion de rareté.
// =============================================================
public static class ItemDropFactory
{
    public static InventoryItem CreateInventoryItem(ScriptableObject itemSO, int quantity, int? rarityOverride = null)
    {
        if (itemSO == null) return null;

        switch (itemSO)
        {
            case WeaponData wd:
                return new InventoryItem(wd.CreateDropInstance(rarityOverride ?? WeaponData.RollRarity()));

            case ArmorData ad:
                return new InventoryItem(ad.CreateDropInstance(rarityOverride ?? ArmorData.RollRarity()));

            case HelmetData hd:
                return new InventoryItem(hd.CreateInstance());

            case GlovesData gd:
                return new InventoryItem(gd.CreateInstance());

            case BootsData bd:
                return new InventoryItem(bd.CreateInstance());

            case JewelryData jd:
                return new InventoryItem(jd.CreateInstance());

            case SpiritData sd:
                return new InventoryItem(new SpiritInstance(sd));

            case ConsumableData cd:
                return new InventoryItem(cd.CreateInstance(quantity));

            case ResourceData rd:
                return new InventoryItem(rd.CreateInstance(quantity));

            case GemData gemD:
                return new InventoryItem(gemD.CreateDropInstance());

            case RuneData runeD:
                return new InventoryItem(runeD.CreateDropInstance());

            default:
                Debug.LogWarning($"[ItemDropFactory] Type SO non reconnu : {itemSO.GetType().Name}");
                return null;
        }
    }
}
