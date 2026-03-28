using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

// =============================================================
// WORLDPICKUPITEM — Item ou pile d'Aeris au sol cliquable
// Path : Assets/Scripts/World/WorldPickupItem.cs
// AetherTree GDD v3.5
//
// Remplace WorldLootItem.cs ET WorldAerisItem.cs.
// Le type est déterminé par pickupType (Item ou Aeris).
//
// Spawné par LootManager à la mort d'un mob.
// Le clic passe par TargetingSystem — plus aucune référence
// à LootApproach (supprimé).
//
// Setup prefab :
//   - Mesh/Sprite visible
//   - Collider (non-trigger) pour le raycast
//   - Ce script sur le GO racine
//   - (optionnel) TextMeshPro pour le montant Aeris
// =============================================================

public enum PickupType { Item, Aeris }

public class WorldPickupItem : MonoBehaviour
{
    // ── Type ──────────────────────────────────────────────────
    [Header("Type")]
    public PickupType pickupType = PickupType.Item;

    // ── Durée de vie ──────────────────────────────────────────
    [Header("Durée de vie")]
    [Tooltip("Durée de vie en secondes avant disparition automatique.")]
    public float lifetime  = 60f;
    [Tooltip("Clignote pendant les X dernières secondes.")]
    public float blinkTime = 10f;

    // ── Ramassage ─────────────────────────────────────────────
    [Header("Ramassage")]
    [Tooltip("Distance max de ramassage en unités world.")]
    public float pickupRange = 3f;

    // ── Aeris ─────────────────────────────────────────────────
    [Header("Aeris (ignoré si pickupType = Item)")]
    public TextMeshPro amountText;

    // ── Données runtime ───────────────────────────────────────
    private InventoryItem _item;
    private int           _aerisAmount;
    private float         _timer;
    private Renderer[]    _renderers;
    private bool          _isBlinking;

    // =========================================================
    // INIT
    // =========================================================

    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>();
    }

    /// <summary>Initialise un item au sol (WorldLootItem).</summary>
    public void InitItem(InventoryItem item, float customLifetime = -1f)
    {
        pickupType = PickupType.Item;
        _item      = item;
        _timer     = customLifetime > 0f ? customLifetime : lifetime;
        if (item != null) gameObject.name = $"Loot_{item.Name}";
    }

    /// <summary>Initialise une pile d'Aeris au sol (WorldAerisItem).</summary>
    public void InitAeris(int amount, float customLifetime = -1f)
    {
        pickupType   = PickupType.Aeris;
        _aerisAmount = amount;
        _timer       = customLifetime > 0f ? customLifetime : lifetime;
        gameObject.name = $"Aeris_{amount}";

        if (amountText != null)
            amountText.text = $"{amount} ¤";
    }

    // =========================================================
    // UPDATE — durée de vie + clignotement
    // =========================================================

    private void Update()
    {
        _timer -= Time.deltaTime;

        if (_timer <= blinkTime && !_isBlinking)
            _isBlinking = true;

        if (_isBlinking && _renderers != null)
        {
            float alpha = Mathf.PingPong(Time.time * 4f, 1f);
            foreach (var r in _renderers) SetAlpha(r, alpha);
        }

        if (_timer <= 0f) Destroy(gameObject);
    }

    // =========================================================
    // CLIC
    // Le premier clic sélectionne dans TargetingSystem.
    // Le deuxième clic (ou si déjà à portée) ramasse.
    // TargetingSystem gère l'approche via HandleLootClick /
    // HandleAerisClick — aucune logique d'approche ici.
    // =========================================================

    private void OnMouseDown()
    {
        // Laisse le raycast de TargetingSystem gérer le clic —
        // OnMouseDown est uniquement un fallback si le raycast
        // ne détecte pas ce collider (couches, etc.)
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // Tente le ramassage direct si déjà à portée
        TryPickUp();
    }

    /// <summary>
    /// Appelé par TargetingSystem quand le joueur est à portée.
    /// </summary>
    public void TryPickUp()
    {
        switch (pickupType)
        {
            case PickupType.Item:   PickUpItem();  break;
            case PickupType.Aeris:  PickUpAeris(); break;
        }
    }

    // =========================================================
    // RAMASSAGE ITEM
    // =========================================================

    private void PickUpItem()
    {
        if (_item == null) return;

        if (!IsInRange()) return;

        var inventory = InventorySystem.Instance;
        if (inventory == null) { Debug.LogWarning("[LOOT] InventorySystem introuvable !"); return; }

        if (inventory.IsFull)
        {
            FloatingText.Spawn("Inventaire plein !", transform.position, Color.red, 1.5f);
            return;
        }

        if (inventory.AddItem(_item))
        {
            FloatingText.Spawn($"+{_item.Name}", transform.position, Color.yellow, 1.5f);
            Destroy(gameObject);
        }
    }

    // =========================================================
    // RAMASSAGE AERIS
    // =========================================================

    private void PickUpAeris()
    {
        if (_aerisAmount <= 0) return;

        if (!IsInRange()) return;

        if (AerisSystem.Instance == null)
        {
            Debug.LogWarning("[AERIS] AerisSystem.Instance est NULL — poser AerisSystem sur _Managers !");
            return;
        }

        AerisSystem.Instance.Add(_aerisAmount);
        FloatingText.Spawn($"+{_aerisAmount} ¤", transform.position, new Color(1f, 0.85f, 0.2f), 1.5f);
        Destroy(gameObject);
    }

    // =========================================================
    // HELPERS
    // =========================================================

    /// <summary>
    /// Vérifie si le joueur est à portée.
    /// Si non, TargetingSystem doit gérer l'approche — ne pas appeler ici.
    /// </summary>
    private bool IsInRange()
    {
        var player = FindObjectOfType<Player>();
        if (player == null) return true; // pas de joueur trouvé → on ramasse quand même

        return Vector3.Distance(player.transform.position, transform.position) <= pickupRange;
    }

    private void SetAlpha(Renderer r, float alpha)
    {
        if (r == null) return;
        foreach (var mat in r.materials)
        {
            Color c = mat.color;
            c.a = alpha;
            mat.color = c;
        }
    }

    // ── Accesseurs pour UI / TargetingSystem ──────────────────
    public string ItemName    => _item?.Name ?? "???";
    public Sprite ItemIcon    => _item?.Icon;
    public int    AerisAmount => _aerisAmount;
}
