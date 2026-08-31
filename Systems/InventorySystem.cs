using System.Collections.Generic;
using UnityEngine;

// =============================================================
// INVENTORYSYSTEM.CS — Gestion des items en mémoire
// Path : Assets/Scripts/Systems/InventorySystem.cs
// AetherTree GDD v30
//
// Conteneur runtime pour tous les items du joueur (non équipés).
// Chaque item est stocké comme un InventoryItem (wrapper générique).
// Publie OnInventoryChanged après chaque modification.
//
// Usage :
//   InventorySystem.Instance.AddItem(new InventoryItem(weaponInstance))
//   InventorySystem.Instance.RemoveItem(item)
//   InventorySystem.Instance.GetItems(EquipmentSlot.Weapon)
// =============================================================

public class InventorySystem : MonoBehaviour
{
    public static InventorySystem Instance { get; private set; }

    // Taille max de l'inventaire (GDD — à ajuster)
    public const int MAX_SLOTS = 80;

    private readonly List<InventoryItem> _items = new List<InventoryItem>();

    public System.Action OnInventoryChanged;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Lecture ───────────────────────────────────────────────

    public List<InventoryItem> GetItems(EquipmentSlot slot)
    {
        var result = new List<InventoryItem>();
        foreach (var item in _items)
            if (item.Slot == slot) result.Add(item);
        return result;
    }

    public int  Count  => _items.Count;
    public bool IsFull => _items.Count >= MAX_SLOTS;

    public List<InventoryItem> GetConsommables()
    {
        var result = new List<InventoryItem>();
        foreach (var item in _items)
            if (item.ItemCategory == InventoryCategory.Consommable) result.Add(item);
        return result;
    }

    public List<InventoryItem> GetRessources()
    {
        var result = new List<InventoryItem>();
        foreach (var item in _items)
            if (item.ItemCategory == InventoryCategory.Ressource) result.Add(item);
        return result;
    }

    public List<InventoryItem> GetCosmetiquesHead()
    {
        var result = new List<InventoryItem>();
        foreach (var item in _items)
            if (item.ItemCategory == InventoryCategory.CosmeticHead) result.Add(item);
        return result;
    }

    public List<InventoryItem> GetCosmetiquesBody()
    {
        var result = new List<InventoryItem>();
        foreach (var item in _items)
            if (item.ItemCategory == InventoryCategory.CosmeticBody) result.Add(item);
        return result;
    }

    public List<InventoryItem> GetCards()
    {
        var result = new List<InventoryItem>();
        foreach (var item in _items)
            if (item.ItemCategory == InventoryCategory.Card) result.Add(item);
        return result;
    }

    // ── Accès complet ─────────────────────────────────────────

    /// <summary>Retourne une copie de tous les items (pour la sauvegarde).</summary>
    public List<InventoryItem> GetAllItems() => new List<InventoryItem>(_items);

    // ── Recherche par instance ────────────────────────────────

    /// <summary>Trouve l'InventoryItem existant dans _items qui contient cette instance.</summary>
    public InventoryItem GetItemByInstance(object instance)
    {
        if (instance == null) return null;
        foreach (var item in _items)
        {
            if (item.WeaponInstance       == instance) return item;
            if (item.ArmorInstance        == instance) return item;
            if (item.HelmetInstance       == instance) return item;
            if (item.GlovesInstance       == instance) return item;
            if (item.BootsInstance        == instance) return item;
            if (item.JewelryInstance      == instance) return item;
            if (item.SpiritInstance       == instance) return item;
            if (item.ConsumableInstance   == instance) return item;
            if (item.ResourceInstance     == instance) return item;
            if (item.CosmeticInstanceHead == instance) return item;
            if (item.CosmeticInstanceBody == instance) return item;
            if (item.CardInstance         == instance) return item;
        }
        return null;
    }

    // ── Ajout ─────────────────────────────────────────────────

    public bool AddItem(InventoryItem item)
    {
        if (item == null) return false;

        // ── Stacking — Ressources ────────────────────────────
        if (item.ResourceInstance != null)
        {
            var existing = FindStackableResource(item.ResourceInstance.data);
            if (existing != null)
            {
                int overflow = existing.ResourceInstance.Add(item.ResourceInstance.quantity);
                OnInventoryChanged?.Invoke();

                if (overflow > 0)
                    return AddItem(new InventoryItem(item.ResourceInstance.data.CreateInstance(overflow)));

                return true;
            }
        }

        // ── Stacking — Consommables ──────────────────────────
        if (item.ConsumableInstance != null)
        {
            var existing = FindStackableConsumable(item.ConsumableInstance.data);
            if (existing != null)
            {
                int overflow = existing.ConsumableInstance.Add(item.ConsumableInstance.quantity);
                OnInventoryChanged?.Invoke();

                if (overflow > 0)
                    return AddItem(new InventoryItem(item.ConsumableInstance.data.CreateInstance(overflow)));

                return true;
            }
        }

        // ── Pas de stack existant — nouveau slot ─────────────
        if (IsFull)
        {
            Debug.LogWarning("[INVENTORY] Inventaire plein !");
            return false;
        }

        _items.Add(item);
        OnInventoryChanged?.Invoke();
        return true;
    }

    /// <summary>Cherche un slot ressource du même SO avec de la place disponible.</summary>
    private InventoryItem FindStackableResource(ResourceData data)
    {
        if (data == null) return null;
        foreach (var item in _items)
            if (item.ResourceInstance?.data == data && item.ResourceInstance.quantity < item.ResourceInstance.MaxStack)
                return item;
        return null;
    }

    /// <summary>Cherche un slot consommable du même SO avec de la place disponible.</summary>
    private InventoryItem FindStackableConsumable(ConsumableData data)
    {
        if (data == null) return null;
        foreach (var item in _items)
            if (item.ConsumableInstance?.data == data && item.ConsumableInstance.quantity < item.ConsumableInstance.MaxStack)
                return item;
        return null;
    }

    // ── Suppression ───────────────────────────────────────────

    public bool RemoveItem(InventoryItem item)
    {
        if (item == null || !_items.Contains(item)) return false;
        _items.Remove(item);
        OnInventoryChanged?.Invoke();
        return true;
    }

    // ── Ressources — comptage/consommation par référence SO ──────
    // Une ressource peut être répartie sur plusieurs stacks (débordement
    // au-delà de MaxStack, voir AddItem) — ces deux méthodes agrègent/
    // consomment sur l'ensemble des stacks du même ResourceData.

    public int GetResourceCount(ResourceData data)
    {
        if (data == null) return 0;
        int total = 0;
        foreach (var item in _items)
            if (item.ResourceInstance?.data == data) total += item.ResourceInstance.quantity;
        return total;
    }

    /// <summary>Retire `amount` du ResourceData donné, réparti sur les stacks existants
    /// (les stacks vidés sont retirés de l'inventaire). Ne retire rien si le total
    /// disponible est insuffisant — vérifier avec GetResourceCount() avant si besoin
    /// d'un contrôle séparé.</summary>
    public bool ConsumeResource(ResourceData data, int amount)
    {
        if (data == null || amount <= 0) return false;
        if (GetResourceCount(data) < amount) return false;

        int remaining = amount;
        foreach (var item in new List<InventoryItem>(_items))
        {
            if (remaining <= 0) break;
            if (item.ResourceInstance?.data != data) continue;

            int take = Mathf.Min(remaining, item.ResourceInstance.quantity);
            item.ResourceInstance.Remove(take);
            remaining -= take;
            if (item.ResourceInstance.IsEmpty) RemoveItem(item);
        }

        OnInventoryChanged?.Invoke();
        return true;
    }

    // ── Équipement depuis l'inventaire ────────────────────────

    /// <summary>
    /// Équipe l'item sur le joueur et le retire de l'inventaire.
    /// L'item actuellement équipé est retourné dans l'inventaire.
    /// </summary>
    public bool EquipItem(InventoryItem item, Player player)
    {
        if (item == null || player == null) return false;

        switch (item.Slot)
        {
            case EquipmentSlot.Weapon:
                if (item.WeaponInstance == null) return false;

                // Vérification compatibilité AVANT de toucher à l'équipé actuel
                if (item.WeaponInstance.data != null
                    && item.WeaponInstance.Category != player.weaponCategory)
                {
                    Debug.LogWarning($"[INVENTORY] Arme incompatible — {item.WeaponInstance.WeaponName} " +
                                    $"({item.WeaponInstance.Category}) refusée pour joueur {player.weaponCategory}.");
                    return false;
                }

                if (player.equippedWeaponInstance?.data != null)
                    AddItem(new InventoryItem(player.equippedWeaponInstance));
                player.EquipWeapon(item.WeaponInstance);
                break;

            case EquipmentSlot.Armor:
                if (item.ArmorInstance == null) return false;
                if (player.equippedArmorInstance?.data != null)
                    AddItem(new InventoryItem(player.equippedArmorInstance));
                player.EquipArmor(item.ArmorInstance);
                break;

            case EquipmentSlot.Helmet:
                if (item.HelmetInstance == null) return false;
                if (player.equippedHelmetInstance?.data != null)
                    AddItem(new InventoryItem(player.equippedHelmetInstance));
                player.EquipHelmet(item.HelmetInstance);
                break;

            case EquipmentSlot.Gloves:
                if (item.GlovesInstance == null) return false;
                if (player.equippedGlovesInstance?.data != null)
                    AddItem(new InventoryItem(player.equippedGlovesInstance));
                player.EquipGloves(item.GlovesInstance);
                break;

            case EquipmentSlot.Boots:
                if (item.BootsInstance == null) return false;
                if (player.equippedBootsInstance?.data != null)
                    AddItem(new InventoryItem(player.equippedBootsInstance));
                player.EquipBoots(item.BootsInstance);
                break;

            case EquipmentSlot.Ring:
            case EquipmentSlot.Necklace:
            case EquipmentSlot.Bracelet:
                if (item.JewelryInstance == null) return false;
                JewelryInstance existingJewelry = player.equippedJewelryInstances?.Find(
                    j => j != null && j.Slot == item.JewelryInstance.Slot);
                if (existingJewelry != null)
                {
                    player.UnequipJewelry(existingJewelry);
                    AddItem(new InventoryItem(existingJewelry));
                }
                player.EquipJewelry(item.JewelryInstance);
                break;

            case EquipmentSlot.Spirit:
                if (item.SpiritInstance == null) return false;
                if (player.equippedSpiritInstances?.Count > 0)
                {
                    var oldSpirit = player.equippedSpiritInstances[0];
                    player.UnequipSpirit(oldSpirit);
                    AddItem(new InventoryItem(oldSpirit));
                }
                player.EquipSpirit(item.SpiritInstance);
                break;

            case EquipmentSlot.Card:
                if (item.CardInstance == null) return false;
                if (player.equippedCardInstance?.data != null)
                    AddItem(new InventoryItem(player.equippedCardInstance));
                player.EquipCard(item.CardInstance);
                break;

            case EquipmentSlot.CosmeticHead:
                if (item.CosmeticInstanceHead == null) return false;
                if (player.equippedCosmeticHeadInstance?.data != null)
                    AddItem(new InventoryItem(player.equippedCosmeticHeadInstance));
                player.EquipCosmeticHead(item.CosmeticInstanceHead);
                break;

            case EquipmentSlot.CosmeticBody:
                if (item.CosmeticInstanceBody == null) return false;
                if (player.equippedCosmeticBodyInstance?.data != null)
                    AddItem(new InventoryItem(player.equippedCosmeticBodyInstance));
                player.EquipCosmeticBody(item.CosmeticInstanceBody);
                break;

            default:
                Debug.LogWarning($"[INVENTORY] Slot {item.Slot} non géré.");
                return false;
        }

        if (!RemoveItem(item))
            RemoveItemByContent(item);

        OnInventoryChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Retire un item en comparant le contenu (instance) plutôt que la référence.
    /// Utile quand l'item vient d'un drag depuis le CharacterPanel (nouveau wrapper).
    /// </summary>
    private void RemoveItemByContent(InventoryItem item)
    {
        InventoryItem toRemove = null;

        foreach (var existing in _items)
        {
            if (item.WeaponInstance       != null && existing.WeaponInstance       == item.WeaponInstance)       { toRemove = existing; break; }
            if (item.ArmorInstance        != null && existing.ArmorInstance        == item.ArmorInstance)        { toRemove = existing; break; }
            if (item.HelmetInstance       != null && existing.HelmetInstance       == item.HelmetInstance)       { toRemove = existing; break; }
            if (item.GlovesInstance       != null && existing.GlovesInstance       == item.GlovesInstance)       { toRemove = existing; break; }
            if (item.BootsInstance        != null && existing.BootsInstance        == item.BootsInstance)        { toRemove = existing; break; }
            if (item.JewelryInstance      != null && existing.JewelryInstance      == item.JewelryInstance)      { toRemove = existing; break; }
            if (item.SpiritInstance       != null && existing.SpiritInstance       == item.SpiritInstance)       { toRemove = existing; break; }
            if (item.ConsumableInstance   != null && existing.ConsumableInstance   == item.ConsumableInstance)   { toRemove = existing; break; }
            if (item.ResourceInstance     != null && existing.ResourceInstance     == item.ResourceInstance)     { toRemove = existing; break; }
            if (item.CosmeticInstanceHead != null && existing.CosmeticInstanceHead == item.CosmeticInstanceHead) { toRemove = existing; break; }
            if (item.CosmeticInstanceBody != null && existing.CosmeticInstanceBody == item.CosmeticInstanceBody) { toRemove = existing; break; }
            if (item.CardInstance         != null && existing.CardInstance         == item.CardInstance)         { toRemove = existing; break; }
        }

        if (toRemove != null) _items.Remove(toRemove);
    }

    /// <summary>Déséquipe l'item du joueur et le place dans l'inventaire.</summary>
    public void UnequipToInventory(EquipmentSlot slot, Player player)
    {
        if (player == null) return;

        switch (slot)
        {
            case EquipmentSlot.Weapon:
                if (player.equippedWeaponInstance != null)
                { AddItem(new InventoryItem(player.equippedWeaponInstance)); player.UnequipWeapon(); }
                break;
            case EquipmentSlot.Armor:
                if (player.equippedArmorInstance != null)
                { AddItem(new InventoryItem(player.equippedArmorInstance)); player.UnequipArmor(); }
                break;
            case EquipmentSlot.Helmet:
                if (player.equippedHelmetInstance != null)
                { AddItem(new InventoryItem(player.equippedHelmetInstance)); player.UnequipHelmet(); }
                break;
            case EquipmentSlot.Gloves:
                if (player.equippedGlovesInstance != null)
                { AddItem(new InventoryItem(player.equippedGlovesInstance)); player.UnequipGloves(); }
                break;
            case EquipmentSlot.Boots:
                if (player.equippedBootsInstance != null)
                { AddItem(new InventoryItem(player.equippedBootsInstance)); player.UnequipBoots(); }
                break;
            case EquipmentSlot.Card:
                if (player.equippedCardInstance != null)
                { AddItem(new InventoryItem(player.equippedCardInstance)); player.UnequipCard(); }
                break;
            case EquipmentSlot.CosmeticHead:
                if (player.equippedCosmeticHeadInstance != null)
                { AddItem(new InventoryItem(player.equippedCosmeticHeadInstance)); player.UnequipCosmeticHead(); }
                break;
            case EquipmentSlot.CosmeticBody:
                if (player.equippedCosmeticBodyInstance != null)
                { AddItem(new InventoryItem(player.equippedCosmeticBodyInstance)); player.UnequipCosmeticBody(); }
                break;
        }
    }

    // ── Sauvegarde / Chargement ───────────────────────────────

    /// <summary>
    /// Déséquipe tous les slots du joueur sans remettre les items dans l'inventaire.
    /// Appelé par SaveSystem avant de recharger une sauvegarde — évite les doublons
    /// avec les items de départ équipés dans Player.Awake().
    /// </summary>
    public void UnequipAll(Player player)
    {
        if (player == null) return;

        player.UnequipWeapon();
        player.UnequipArmor();
        player.UnequipHelmet();
        player.UnequipGloves();
        player.UnequipBoots();
        player.UnequipCard();
        player.UnequipCosmeticHead();
        player.UnequipCosmeticBody();

        if (player.equippedJewelryInstances != null)
        {
            var copy = new List<JewelryInstance>(player.equippedJewelryInstances);
            foreach (var j in copy)
                if (j != null) player.UnequipJewelry(j);
        }

        if (player.equippedSpiritInstances != null)
        {
            var copy = new List<SpiritInstance>(player.equippedSpiritInstances);
            foreach (var s in copy)
                if (s != null) player.UnequipSpirit(s);
        }
    }

    /// <summary>
    /// Vide complètement l'inventaire (items non équipés).
    /// Appelé par SaveSystem avant de recharger une sauvegarde.
    /// </summary>
    public void ClearAll()
    {
        _items.Clear();
        OnInventoryChanged?.Invoke();
    }
}

// =============================================================
// INVENTORYCATEGORY — catégorie principale d'un item
// =============================================================
public enum InventoryCategory
{
    Equipement,   // Weapon, Armor, Helmet, Gloves, Boots, Jewelry, Spirit
    Consommable,  // Potion, Pierre de donjon, Téléportation
    Ressource,    // Matériaux craft, Ingrédients cuisine, Drops mobs
    CosmeticHead, // Cosmétique tête
    CosmeticBody, // Cosmétique corps
    Card,         // Cartes
}

// =============================================================
// INVENTORYITEM — Wrapper générique pour tout type d'item
// =============================================================
public class InventoryItem
{
    // ── Instances équipement ──────────────────────────────────
    public WeaponInstance     WeaponInstance     { get; private set; }
    public ArmorInstance      ArmorInstance      { get; private set; }
    public HelmetInstance     HelmetInstance     { get; private set; }
    public GlovesInstance     GlovesInstance     { get; private set; }
    public BootsInstance      BootsInstance      { get; private set; }
    public JewelryInstance    JewelryInstance    { get; private set; }
    public SpiritInstance     SpiritInstance     { get; private set; }

    // ── Instances consommables ────────────────────────────────
    public ConsumableInstance ConsumableInstance { get; private set; }
    public RuneInstance       RuneInstance       { get; private set; }
    public GemInstance        GemInstance        { get; private set; }
    public CardInstance       CardInstance       { get; private set; }

    // ── Instances ressources ──────────────────────────────────
    public ResourceInstance   ResourceInstance   { get; private set; }

    // ── Instances cosmétiques ─────────────────────────────────
    public CosmeticInstanceHead CosmeticInstanceHead { get; private set; }
    public CosmeticInstanceBody CosmeticInstanceBody { get; private set; }

    // ── Catégorie & Slot ──────────────────────────────────────
    public InventoryCategory ItemCategory { get; private set; }
    public EquipmentSlot     Slot         { get; private set; }

    // ── Nom ───────────────────────────────────────────────────
    public string Name
    {
        get
        {
            if (WeaponInstance       != null) return WeaponInstance.WeaponName;
            if (ArmorInstance        != null) return ArmorInstance.ArmorName;
            if (HelmetInstance       != null) return HelmetInstance.HelmetName;
            if (GlovesInstance       != null) return GlovesInstance.GlovesName;
            if (BootsInstance        != null) return BootsInstance.BootsName;
            if (JewelryInstance      != null) return JewelryInstance.JewelryName;
            if (SpiritInstance       != null) return SpiritInstance.SpiritName;
            if (ConsumableInstance   != null) return ConsumableInstance.Name;
            if (RuneInstance         != null) return RuneInstance.RuneName;
            if (GemInstance          != null) return GemInstance.GemName;
            if (ResourceInstance     != null) return ResourceInstance.Name;
            if (CosmeticInstanceHead != null) return CosmeticInstanceHead.CosmeticName;
            if (CosmeticInstanceBody != null) return CosmeticInstanceBody.CosmeticName;
            if (CardInstance         != null) return CardInstance.CardName;
            return "???";
        }
    }

    // ── Icône ─────────────────────────────────────────────────
    public Sprite Icon
    {
        get
        {
            if (WeaponInstance?.data       != null) return WeaponInstance.Icon;
            if (ArmorInstance?.data        != null) return ArmorInstance.Icon;
            if (HelmetInstance?.data       != null) return HelmetInstance.Icon;
            if (GlovesInstance?.data       != null) return GlovesInstance.Icon;
            if (BootsInstance?.data        != null) return BootsInstance.Icon;
            if (JewelryInstance?.data      != null) return JewelryInstance.Icon;
            if (SpiritInstance?.data       != null) return SpiritInstance.Icon;
            if (ConsumableInstance?.data   != null) return ConsumableInstance.Icon;
            if (RuneInstance?.data         != null) return RuneInstance.Icon;
            if (GemInstance?.data          != null) return GemInstance.Icon;
            if (ResourceInstance?.data     != null) return ResourceInstance.Icon;
            if (CosmeticInstanceHead?.data != null) return CosmeticInstanceHead.Icon;
            if (CosmeticInstanceBody?.data != null) return CosmeticInstanceBody.Icon;
            if (CardInstance?.data         != null) return CardInstance.Icon;
            return null;
        }
    }

    // ── Rareté / Quantité (affiché dans Count sur la cellule) ─
    public string CountLabel
    {
        get
        {
            if (WeaponInstance       != null) return WeaponInstance.RarityLabel;
            if (ArmorInstance        != null) return ArmorInstance.RarityLabel;
            if (RuneInstance         != null) return RuneInstance.RarityLabel;
            if (GemInstance          != null) return $"Lv{GemInstance.GemLevel}";
            if (CosmeticInstanceHead != null) return "";   // pas de rareté sur les cosmétiques
            if (CosmeticInstanceBody != null) return "";
            if (CardInstance         != null) return "";   // TODO: rareté carte à définir
            if (ConsumableInstance   != null) return ConsumableInstance.quantity > 1 ? $"x{ConsumableInstance.quantity}" : "";
            if (ResourceInstance     != null) return ResourceInstance.quantity   > 1 ? $"x{ResourceInstance.quantity}"   : "";
            return "";
        }
    }

    // ── Rétrocompat ───────────────────────────────────────────
    public string RarityLabel => CountLabel;

    /// <summary>Texte affiché dans la cellule inventaire — quantité pour stackables, rien pour équipements.</summary>
    public string CellLabel
    {
        get
        {
            if (ConsumableInstance != null) return ConsumableInstance.quantity > 1 ? $"x{ConsumableInstance.quantity}" : "";
            if (ResourceInstance   != null) return ResourceInstance.quantity   > 1 ? $"x{ResourceInstance.quantity}"   : "";
            return "";
        }
    }

    // ── Constructeurs équipement ──────────────────────────────
    public InventoryItem(WeaponInstance i)
    { WeaponInstance = i; Slot = EquipmentSlot.Weapon; ItemCategory = InventoryCategory.Equipement; }

    public InventoryItem(ArmorInstance i)
    { ArmorInstance = i; Slot = EquipmentSlot.Armor; ItemCategory = InventoryCategory.Equipement; }

    public InventoryItem(HelmetInstance i)
    { HelmetInstance = i; Slot = EquipmentSlot.Helmet; ItemCategory = InventoryCategory.Equipement; }

    public InventoryItem(GlovesInstance i)
    { GlovesInstance = i; Slot = EquipmentSlot.Gloves; ItemCategory = InventoryCategory.Equipement; }

    public InventoryItem(BootsInstance i)
    { BootsInstance = i; Slot = EquipmentSlot.Boots; ItemCategory = InventoryCategory.Equipement; }

    public InventoryItem(JewelryInstance i)
    {
        JewelryInstance = i;
        ItemCategory    = InventoryCategory.Equipement;
        Slot = i.Slot == JewelrySlot.Ring     ? EquipmentSlot.Ring
             : i.Slot == JewelrySlot.Necklace ? EquipmentSlot.Necklace
                                              : EquipmentSlot.Bracelet;
    }

    public InventoryItem(SpiritInstance i)
    { SpiritInstance = i; Slot = EquipmentSlot.Spirit; ItemCategory = InventoryCategory.Equipement; }

    // ── Constructeurs consommables ────────────────────────────
    public InventoryItem(ConsumableInstance i)
    { ConsumableInstance = i; Slot = EquipmentSlot.Weapon; ItemCategory = InventoryCategory.Consommable; }
    // ⚠ EquipmentSlot n'a pas de valeur Consommable/Resource — on réutilise Weapon comme slot neutre
    // pour les non-équipements. Le filtre se fait via ItemCategory, pas Slot.

    public InventoryItem(RuneInstance i)
    { RuneInstance = i; Slot = EquipmentSlot.Weapon; ItemCategory = InventoryCategory.Consommable; }

    public InventoryItem(GemInstance i)
    { GemInstance = i; Slot = EquipmentSlot.Weapon; ItemCategory = InventoryCategory.Consommable; }

    // ── Constructeurs ressources ──────────────────────────────
    public InventoryItem(ResourceInstance i)
    { ResourceInstance = i; Slot = EquipmentSlot.Weapon; ItemCategory = InventoryCategory.Ressource; }

    // ── Constructeurs cosmétiques ─────────────────────────────
    public InventoryItem(CosmeticInstanceHead i)
    { CosmeticInstanceHead = i; Slot = EquipmentSlot.CosmeticHead; ItemCategory = InventoryCategory.CosmeticHead; }

    public InventoryItem(CosmeticInstanceBody i)
    { CosmeticInstanceBody = i; Slot = EquipmentSlot.CosmeticBody; ItemCategory = InventoryCategory.CosmeticBody; }

    // ── Constructeur carte ────────────────────────────────────
    public InventoryItem(CardInstance i)
    { CardInstance = i; Slot = EquipmentSlot.Card; ItemCategory = InventoryCategory.Card; }
}
