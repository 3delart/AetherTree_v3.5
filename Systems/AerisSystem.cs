using UnityEngine;

// =============================================================
// AERISSYSTEM — Gestion de la monnaie Aeris
// Path : Assets/Scripts/Systems/AerisSystem.cs
// AetherTree GDD v30 — Section 9.2
//
// Source unique pour le montant d'Aeris du joueur.
// Persisté via SaveSystem (CharacterProgress.aeris), au même titre que
// le reste de la progression — plus de PlayerPrefs séparé (ancien
// mécanisme, désynchronisé du fichier de save JSON : supprimer la save
// ne remettait pas l'Aeris à zéro). SetAeris() est appelé par
// SaveSystem.ApplyProgress() au chargement.
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
    }

    // =========================================================
    // API
    // =========================================================

    public void Resubscribe() { } // plus d'abonnement — Aeris spawned au sol par LootManager

    public void Add(int amount)
    {
        if (amount <= 0) return;
        _aeris += amount;
        OnAerisChanged?.Invoke(_aeris);
    }

    public bool Spend(int amount)
    {
        if (amount > _aeris) return false;
        _aeris -= amount;
        OnAerisChanged?.Invoke(_aeris);
        return true;
    }

    /// <summary>Fixe directement le montant — appelé par SaveSystem.ApplyProgress() au
    /// chargement, pour que la valeur sauvegardée fasse autorité (contrairement à
    /// Add()/Spend(), relatifs et destinés au gameplay).</summary>
    public void SetAeris(int amount)
    {
        _aeris = Mathf.Max(0, amount);
        OnAerisChanged?.Invoke(_aeris);
    }
}
