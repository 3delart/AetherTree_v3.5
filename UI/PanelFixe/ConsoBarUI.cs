using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// =============================================================
// CONSOBARUI.CS — Barre des 3 consommables quickslot
// AetherTree GDD v18
//
// Glisser les 3 GameObjects ConsoSlot1…3 dans slots[].
// Les enfants (ItemIcon, CDOverlay, Qty, Key) trouvés par nom — CDOverlay =
// Image radial fill (comme SkillBar/PassifBar), pas de texte countdown ici.
// Key = raccourci affiché (F1/F2/F3, câblage clavier réel pas encore fait).
// Utilisation : clic OU F1 / F2 / F3
// =============================================================

public class ConsoBarUI : MonoBehaviour
{
    public static ConsoBarUI Instance { get; private set; }

    [Header("Slots — glisser les 3 GameObjects ici")]
    public GameObject[] slots = new GameObject[3];

    private ConsoSlotBarUI[] _slotUIs = new ConsoSlotBarUI[3];
    private Player           _player;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            Transform t = slots[i].transform;

            ConsoSlotBarUI slot = slots[i].GetComponent<ConsoSlotBarUI>();
            if (slot == null) slot = slots[i].AddComponent<ConsoSlotBarUI>();

            slot.itemIcon     = t.Find("ItemIcon")  ?.GetComponent<Image>();
            slot.cdOverlay    = t.Find("CDOverlay") ?.GetComponent<Image>();
            slot.keyText      = t.Find("Key")       ?.GetComponent<TextMeshProUGUI>();
            slot.quantityText = t.Find("Qty")       ?.GetComponent<TextMeshProUGUI>();
            slot.Init();

            _slotUIs[i] = slot;

            // Drop zone — accepte les consommables depuis l'inventaire
            var drop = slots[i].GetComponent<ConsoDropSlot>();
            if (drop == null) drop = slots[i].AddComponent<ConsoDropSlot>();
            drop.slotIndex = i;

            // Ajoute TooltipTrigger si absent — même pattern que SkillBarUI
            if (slots[i].GetComponent<TooltipTrigger>() == null)
                slots[i].AddComponent<TooltipTrigger>();

            // Clic → utilise le slot
            var btn = slots[i].GetComponent<Button>();
            if (btn != null)
            {
                int captured = i;
                btn.onClick.AddListener(() => TryUseSlot(captured));
            }
        }
    }

    private void Start()
    {
        _player = FindObjectOfType<Player>();
        RefreshAll();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) TryUseSlot(0);
        if (Input.GetKeyDown(KeyCode.F2)) TryUseSlot(1);
        if (Input.GetKeyDown(KeyCode.F3)) TryUseSlot(2);
    }

    /// <summary>Utilise le consommable du slot — Potion (heal HP/Mana + buff) seule
    /// implémentée pour l'instant ; DungeonStone/TeleportItem/Other pas encore câblés.</summary>
    public void TryUseSlot(int index)
    {
        if (index < 0 || index >= _slotUIs.Length || _slotUIs[index] == null) return;

        var slot     = _slotUIs[index];
        var instance = slot.CurrentInstance;
        if (instance == null || instance.data == null || instance.IsEmpty) return;
        if (slot.IsOnCooldown) return;

        if (_player == null) _player = FindObjectOfType<Player>();
        if (_player == null || _player.isDead) return;

        var data = instance.data;
        if (data.consumableType != ConsumableType.Potion && data.consumableType != ConsumableType.Food)
        {
            Debug.Log($"[ConsoBarUI] {data.consumableType} pas encore implémenté à l'usage.");
            return;
        }

        if (data.healHP   > 0f) _player.Heal(data.healHP);
        if (data.healMana > 0f) _player.RecoverMana(data.healMana);
        if (data.buffEffect != null) _player.statusEffects?.ApplyBuff(data.buffEffect, _player);

        instance.Remove(1);
        if (instance.IsEmpty)
        {
            var wrapper = InventorySystem.Instance?.GetAllItems().Find(i => i.ConsumableInstance == instance);
            if (wrapper != null) InventorySystem.Instance.RemoveItem(wrapper);
            slot.SetConsoInstance(null);
        }
        else
        {
            slot.UpdateQuantity(instance.quantity);
        }

        if (data.cooldown > 0f) slot.StartCooldown(data.cooldown);

        InventorySystem.Instance?.OnInventoryChanged?.Invoke();
        InventoryUI.Instance?.RefreshGrid();
    }

    public void RefreshAll()
    {
        for (int i = 0; i < _slotUIs.Length; i++)
            _slotUIs[i]?.SetConso(null, 0);
    }

    public void AssignConso(int index, ConsumableData conso, int quantity)
    {
        if (index >= 0 && index < _slotUIs.Length)
            _slotUIs[index]?.SetConso(conso, quantity);
    }

    public void UpdateQuantity(int index, int quantity)
    {
        if (index >= 0 && index < _slotUIs.Length)
            _slotUIs[index]?.UpdateQuantity(quantity);
    }

    /// <summary>Assigne une ConsumableInstance au slot (appelé par ConsoDropSlot).</summary>
    public void AssignConsoInstance(int index, ConsumableInstance conso)
    {
        if (index < 0 || index >= _slotUIs.Length) return;
        _slotUIs[index]?.SetConsoInstance(conso);
    }

    /// <summary>SO actuellement assigné au slot — utilisé par SaveSystem pour persister
    /// l'assignation (par référence SO, pas par instance — voir CharacterProgress.consoBarSlots).</summary>
    public ConsumableData GetSlotData(int index)
        => (index >= 0 && index < _slotUIs.Length) ? _slotUIs[index]?.CurrentConso : null;
}

// =============================================================
// CONSOSLOTBARUI — attaché automatiquement sur chaque slot
// =============================================================
public class ConsoSlotBarUI : MonoBehaviour
{
    [HideInInspector] public Image           itemIcon;
    [HideInInspector] public Image           cdOverlay;
    [HideInInspector] public TextMeshProUGUI cdText;
    [HideInInspector] public TextMeshProUGUI keyText;
    [HideInInspector] public TextMeshProUGUI quantityText;

    private ConsumableData _currentConso;
    public  ConsumableData CurrentConso => _currentConso;

    private float _cooldownRemaining = 0f;
    private float _cooldownTotal     = 0f;
    public  bool  IsOnCooldown => _cooldownRemaining > 0f;

    private static readonly Color EmptyColor  = new Color(0f, 0f, 0f, 0.4f);
    private static readonly Color OnCooldown  = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color DimmedColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    public void Init()
    {
        if (cdOverlay == null) return;
        cdOverlay.type       = Image.Type.Filled;
        cdOverlay.fillMethod = Image.FillMethod.Radial360;
        cdOverlay.fillAmount = 0f;
        cdOverlay.gameObject.SetActive(false);
    }

    private ConsumableInstance _currentInstance;
    public ConsumableInstance CurrentInstance => _currentInstance;

    /// <summary>Assigne une ConsumableInstance depuis l'inventaire (drag & drop).</summary>
    public void SetConsoInstance(ConsumableInstance instance)
    {
        _currentInstance = instance;
        _currentConso    = instance?.data;

        if (itemIcon != null)
        {
            if (instance == null)
            {
                itemIcon.sprite  = null;
                itemIcon.color   = EmptyColor;
                itemIcon.enabled = false;
            }
            else
            {
                itemIcon.sprite  = instance.Icon;
                itemIcon.color   = Color.white;
                itemIcon.enabled = true;
            }
        }
        UpdateQuantity(instance?.quantity ?? 0);
        GetComponent<TooltipTrigger>()?.SetItem(instance != null ? new InventoryItem(instance) : null);
    }

    public void SetConso(ConsumableData conso, int quantity)
    {
        _currentConso    = conso;
        _currentInstance = conso != null ? conso.CreateInstance(quantity) : null;

        if (itemIcon != null)
        {
            if (conso == null)
            {
                itemIcon.sprite  = null;
                itemIcon.color   = EmptyColor;
                itemIcon.enabled = false;
            }
            else
            {
                itemIcon.sprite  = conso.icon;
                itemIcon.color   = Color.white;
                itemIcon.enabled = true;
            }
        }
        UpdateQuantity(quantity);
        GetComponent<TooltipTrigger>()?.SetItem(_currentInstance != null ? new InventoryItem(_currentInstance) : null);
    }

    public void UpdateQuantity(int quantity)
    {
        if (quantityText == null) return;
        quantityText.gameObject.SetActive(quantity > 1);
        quantityText.text = quantity > 1 ? $"x{quantity}" : "";
    }

    public void SetCooldown(float remaining, float total)
    {
        bool onCD = remaining > 0f && total > 0f;
        if (cdOverlay != null) { cdOverlay.gameObject.SetActive(onCD); cdOverlay.fillAmount = onCD ? remaining / total : 0f; cdOverlay.color = onCD ? OnCooldown : Color.clear; }
        if (cdText    != null) { cdText.gameObject.SetActive(onCD);    cdText.text = onCD ? Mathf.CeilToInt(remaining).ToString() : ""; }
        if (itemIcon  != null && _currentConso != null) itemIcon.color = onCD ? DimmedColor : Color.white;
    }

    /// <summary>Démarre le décompte — TryUseSlot() vérifie IsOnCooldown avant de réutiliser.</summary>
    public void StartCooldown(float duration)
    {
        _cooldownTotal     = duration;
        _cooldownRemaining = duration;
        SetCooldown(_cooldownRemaining, _cooldownTotal);
    }

    private void Update()
    {
        if (_cooldownRemaining <= 0f) return;
        _cooldownRemaining -= Time.deltaTime;
        if (_cooldownRemaining < 0f) _cooldownRemaining = 0f;
        SetCooldown(_cooldownRemaining, _cooldownTotal);
    }
}

// =============================================================
// CONSODROPSLOT — Drop zone sur chaque slot de la ConsoBar
// Poser automatiquement par ConsoBarUI.Awake() sur chaque slot.
// Accepte un InventoryItem de type ConsumableInstance.
// =============================================================
public class ConsoDropSlot : MonoBehaviour, IDropHandler
{
    [HideInInspector] public int slotIndex = 0;

    private Player _player;

    private void Start() => _player = UnityEngine.Object.FindObjectOfType<Player>();

    public void OnDrop(PointerEventData e)
    {
        var item = InventoryUI.DraggedItem;
        if (item == null) return;

        // Accepte uniquement les consommables
        if (item.ConsumableInstance == null)
        {
            UnityEngine.Debug.Log($"[CONSO DROP] Seuls les consommables peuvent être glissés ici.");
            return;
        }

        var conso = item.ConsumableInstance;
        ConsoBarUI.Instance?.AssignConsoInstance(slotIndex, conso);
        UnityEngine.Debug.Log($"[CONSO DROP] {conso.Name} assigné au slot {slotIndex}.");
    }
}