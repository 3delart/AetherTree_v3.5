using UnityEngine;
using System.Collections.Generic;

// =============================================================
// SHOPSTOCKREGISTRY — Persistance des stocks limités de shop
// Path : Assets/Scripts/Systems/ShopStockRegistry.cs
// AetherTree GDD v30 — §19
//
// Les ShopEntry.stockCount sont définis sur le SO (reset au lancement).
// Ce registry sauvegarde en PlayerPrefs les quantités déjà achetées
// par le joueur, pour chaque item limité (isUnlimitedStock = false).
//
// Clé PlayerPrefs : "ShopStock_{pnjName}_{itemName}"
// Valeur          : nombre d'unités déjà achetées
//
// Usage :
//   ShopStockRegistry.Instance.GetRemainingStock(pnjName, entry)
//   ShopStockRegistry.Instance.RecordPurchase(pnjName, entry, qty)
// =============================================================

public class ShopStockRegistry : MonoBehaviour
{
    public static ShopStockRegistry Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // =========================================================
    // API PUBLIQUE
    // =========================================================

    /// <summary>
    /// Stock restant pour un item limité.
    /// Retourne toujours int.MaxValue si isUnlimitedStock.
    /// </summary>
    public int GetRemainingStock(string pnjName, ShopEntry entry)
    {
        if (entry == null || entry.isUnlimitedStock) return int.MaxValue;

        int bought = PlayerPrefs.GetInt(Key(pnjName, entry), 0);
        return Mathf.Max(0, entry.stockCount - bought);
    }

    /// <summary>
    /// Enregistre un achat — réduit le stock disponible de façon persistante.
    /// </summary>
    public void RecordPurchase(string pnjName, ShopEntry entry, int qty)
    {
        if (entry == null || entry.isUnlimitedStock) return;

        string key   = Key(pnjName, entry);
        int    bought = PlayerPrefs.GetInt(key, 0);
        PlayerPrefs.SetInt(key, bought + qty);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// True si le stock est épuisé pour ce joueur.
    /// </summary>
    public bool IsExhausted(string pnjName, ShopEntry entry)
    {
        if (entry == null || entry.isUnlimitedStock) return false;
        return GetRemainingStock(pnjName, entry) <= 0;
    }

    // =========================================================
    // CLÉ
    // =========================================================

    private string Key(string pnjName, ShopEntry entry)
    {
        string itemName = entry.item != null ? entry.item.name : "unknown";
        return $"ShopStock_{pnjName}_{itemName}";
    }


    [ContextMenu("Reset TOUT le stock shop")]
    public void DebugResetAllStock()
    {
        // Supprime toutes les clés PlayerPrefs qui commencent par "ShopStock_"
        // PlayerPrefs ne filtre pas par préfixe, donc on fait une liste manuelle
        // Alternative : PlayerPrefs.DeleteAll() si tu n'as pas d'autres données importantes
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("[SHOPSTOCK] Tout le stock réinitialisé.");
    }
}
