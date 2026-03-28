using UnityEngine;

// =============================================================
// AERISSYSTEM — Gestion de la monnaie Aeris
// Path : Assets/Scripts/Systems/AerisSystem.cs
// AetherTree GDD v30 — Section 9.2
//
// Source unique pour le montant d'Aeris du joueur.
// Sauvegardé en PlayerPrefs (sera remplacé par save system).
// S'abonne à OnMobKilled pour collecter les Aeris du loot.
//
// Setup : poser sur _Managers.
// =============================================================
public class AerisSystem : MonoBehaviour
{
    public static AerisSystem Instance { get; private set; }

    private int _aeris = 0;
    public int  Aeris  => _aeris;

    public System.Action<int> OnAerisChanged; // notifie l'UI

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        _aeris = PlayerPrefs.GetInt("Aeris", 0);
    }

    // =========================================================
    // API
    // =========================================================

    public void Resubscribe() { } // plus d'abonnement — Aeris spawned au sol par LootManager

    public void Add(int amount)
    {
        if (amount <= 0) return;
        _aeris += amount;
        Save();
        OnAerisChanged?.Invoke(_aeris);
    }

    public bool Spend(int amount)
    {
        if (amount > _aeris) return false;
        _aeris -= amount;
        Save();
        OnAerisChanged?.Invoke(_aeris);
        return true;
    }

    private void Save() => PlayerPrefs.SetInt("Aeris", _aeris);
}
