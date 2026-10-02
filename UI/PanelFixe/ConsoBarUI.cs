using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;

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

    [Header("Animations (optionnel)")]
    [Tooltip("Jouée à la consommation d'une potion (ConsumableType.Potion uniquement, pas\n" +
             "Food). Null = pas d'anim. Un seul coup via PlayAttack — pas de verrou le temps\n" +
             "du clip, le soin/buff s'applique immédiatement comme avant.")]
    public AnimationClip potionDrinkAnimation;

    private ConsoSlotBarUI[] _slotUIs = new ConsoSlotBarUI[3];
    private Player           _player;

    // ── Cooldown partagé par ConsumableData (pas par slot) ──────────────────
    // Un même potion consommé depuis la barre OU par double-clic inventaire (même item non
    // slotté) partage le MÊME cooldown — sinon le double-clic contournait le CD (trouvé en
    // test manuel : spam de potions identiques non assignées à la barre). Time.time-based,
    // pas de coroutine — ConsoSlotBarUI.Update() lit GetCooldownRemaining() chaque frame pour
    // son affichage, plus de décompte local indépendant.
    private readonly Dictionary<ConsumableData, float> _cooldownEndTime = new Dictionary<ConsumableData, float>();
    private readonly Dictionary<ConsumableData, float> _cooldownDuration = new Dictionary<ConsumableData, float>();

    public bool IsOnCooldown(ConsumableData data)
        => data != null && _cooldownEndTime.TryGetValue(data, out float end) && Time.time < end;

    public float GetCooldownRemaining(ConsumableData data)
        => data != null && _cooldownEndTime.TryGetValue(data, out float end) ? Mathf.Max(0f, end - Time.time) : 0f;

    public float GetCooldownDuration(ConsumableData data)
        => data != null && _cooldownDuration.TryGetValue(data, out float d) ? d : 0f;

    private void StartCooldown(ConsumableData data, float duration)
    {
        if (data == null || duration <= 0f) return;
        _cooldownEndTime[data]  = Time.time + duration;
        _cooldownDuration[data] = duration;
    }

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

    /// <summary>Utilise le consommable du slot — Potion/Food (heal HP/Mana + buff) et
    /// DungeonKey (arme une entrée en attente, voir InstanceSession.ArmEntry — le vrai
    /// chargement de l'instance a lieu au franchissement d'un portail gaté, pas ici)
    /// implémentés ; TeleportItem/Other pas encore câblés.</summary>
    public void TryUseSlot(int index)
    {
        if (index < 0 || index >= _slotUIs.Length || _slotUIs[index] == null) return;

        var slot     = _slotUIs[index];
        var instance = slot.CurrentInstance;
        if (instance == null || instance.data == null || instance.IsEmpty) return;

        UseConsumable(instance, slot);
    }

    /// <summary>Utilise un consommable directement depuis l'inventaire (double clic) — voir
    /// InventoryUI.OnCellDoubleClicked. Passe par le MÊME gate de cooldown partagé (par
    /// ConsumableData) que TryUseSlot() — pas de raccourci qui permettrait de contourner le CD
    /// d'une potion en la gardant hors barre.</summary>
    public void TryUseInstance(ConsumableInstance instance)
    {
        if (instance == null || instance.data == null || instance.IsEmpty) return;

        for (int i = 0; i < _slotUIs.Length; i++)
        {
            if (_slotUIs[i] != null && _slotUIs[i].CurrentInstance == instance)
            {
                TryUseSlot(i);
                return;
            }
        }

        UseConsumable(instance, null);
    }

    /// <summary>Cœur partagé de TryUseSlot()/TryUseInstance() — slot est null quand appelé
    /// depuis l'inventaire sans slot ConsoBar associé (le cooldown reste géré, lui, au niveau
    /// ConsumableData — voir _cooldownEndTime).</summary>
    private void UseConsumable(ConsumableInstance instance, ConsoSlotBarUI slot)
    {
        if (_player == null) _player = FindObjectOfType<Player>();
        if (_player == null || _player.isDead) return;

        var data = instance.data;
        if (IsOnCooldown(data)) return;

        if (data.consumableType == ConsumableType.DungeonKey)
        {
            var dungeon = DungeonRegistry.Instance?.ResolveByKey(data);
            if (dungeon == null)
            {
                Debug.LogWarning($"[ConsoBarUI] Aucun DungeonData ne référence la clé '{data.itemID}' comme requiredKey.");
                return;
            }
            bool armed = InstanceSession.Instance != null && InstanceSession.Instance.ArmEntry(dungeon);
            if (!armed)
            {
                Debug.LogWarning($"[ConsoBarUI] Entrée en attente refusée pour '{dungeon.dungeonID}' — item non consommé.");
                return;
            }
            InstanceSession.Instance.AttachLeaderVfx(data.dungeonEntryVfx, _player);
            ConsumeAndRefresh(instance, slot);
            return;
        }

        if (data.consumableType == ConsumableType.RewardChest)
        {
            var won = data.RollChestEntry(instance.chestRarity ?? WeaponData.RollRarity());
            if (won != null) InventorySystem.Instance?.AddItem(won);
            ConsumeAndRefresh(instance, slot);
            return;
        }

        if (data.consumableType != ConsumableType.Potion && data.consumableType != ConsumableType.Food)
        {
            Debug.Log($"[ConsoBarUI] {data.consumableType} pas encore implémenté à l'usage.");
            return;
        }

        if (data.healHP   > 0f) _player.Heal(data.healHP);
        if (data.healMana > 0f) _player.RecoverMana(data.healMana);
        if (data.buffEffect != null) _player.statusEffects?.ApplyBuff(data.buffEffect, _player);

        if (data.consumableType == ConsumableType.Potion)
            _player.AnimatorController?.PlayAttack(potionDrinkAnimation);

        StartCooldown(data, data.cooldown);
        ConsumeAndRefresh(instance, slot);
    }

    /// <summary>Retire 1 du stack, nettoie/rafraîchit inventaire + slot (si assigné). Le
    /// cooldown est géré séparément par StartCooldown(data, ...) — voir UseConsumable.</summary>
    private void ConsumeAndRefresh(ConsumableInstance instance, ConsoSlotBarUI slot)
    {
        instance.Remove(1);
        if (instance.IsEmpty)
        {
            var wrapper = InventorySystem.Instance?.GetAllItems().Find(i => i.ConsumableInstance == instance);
            if (wrapper != null) InventorySystem.Instance.RemoveItem(wrapper);
            slot?.SetConsoInstance(null);
        }
        else
        {
            slot?.UpdateQuantity(instance.quantity);
        }

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
public class ConsoSlotBarUI : MonoBehaviour, IPointerClickHandler
{
    [HideInInspector] public Image           itemIcon;
    [HideInInspector] public Image           cdOverlay;
    [HideInInspector] public TextMeshProUGUI cdText;
    [HideInInspector] public TextMeshProUGUI keyText;
    [HideInInspector] public TextMeshProUGUI quantityText;

    private ConsumableData _currentConso;
    public  ConsumableData CurrentConso => _currentConso;

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

    /// <summary>Clic droit — vide le slot. Exige l'inventaire ouvert : c'est le geste qui rend le
    /// clic droit VOLONTAIRE (on ne vide jamais un slot par un clic droit machinal en plein
    /// combat), pas une histoire de conflit avec le déplacement (déjà réglé côté
    /// PlayerController). Ne touche pas à l'inventaire, l'objet y reste (la ConsoBar n'est
    /// qu'une référence vers le même stack, voir SetConsoInstance).</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right) return;
        if (_currentInstance == null) return;
        if (InventoryUI.Instance == null || !InventoryUI.Instance.gameObject.activeSelf) return;
        SetConsoInstance(null);
    }

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

    /// <summary>Restaure une assignation depuis la sauvegarde (par référence SO, voir
    /// CharacterProgress.consoBarSlots — pas d'instance à sérialiser). Retrouve la VRAIE
    /// ConsumableInstance déjà présente dans l'inventaire du joueur au lieu d'en fabriquer une
    /// nouvelle : sinon le slot pointerait sur une copie fantôme, jamais reliée au vrai stack
    /// (miroir cassé — vendre l'objet dans l'inventaire ne viderait jamais ce slot).</summary>
    public void SetConso(ConsumableData conso, int quantity)
    {
        if (conso == null) { SetConsoInstance(null); return; }

        ConsumableInstance real = null;
        if (InventorySystem.Instance != null)
        {
            foreach (var item in InventorySystem.Instance.GetAllItems())
            {
                if (item.ConsumableInstance?.data == conso) { real = item.ConsumableInstance; break; }
            }
        }
        SetConsoInstance(real);
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

    // Cooldown affiché ici en LECTURE SEULE — la source de vérité est ConsoBarUI (partagée
    // par ConsumableData, pas par slot, voir ConsoBarUI._cooldownEndTime). Un item identique
    // utilisé depuis l'inventaire (double clic, hors barre) doit assombrir CE slot aussi.
    //
    // Miroir vivant du stack — pas une copie figée au moment du glisser-déposer : ce slot et
    // la cellule d'inventaire pointent sur la MÊME ConsumableInstance. Si le stack change
    // ailleurs (vente, artisanat, une autre UI qui le consomme), la quantité affichée ici doit
    // suivre sans action de la ConsoBar elle-même — d'où la relecture de instance.quantity
    // chaque frame plutôt qu'une valeur mémorisée à l'assignation. Si le stack tombe à 0 et
    // disparaît de l'inventaire, le slot se vide tout seul.
    private void Update()
    {
        if (_currentInstance != null)
        {
            bool stillInInventory = InventorySystem.Instance?.GetItemByInstance(_currentInstance) != null;
            if (_currentInstance.IsEmpty || !stillInInventory)
                SetConsoInstance(null);
            else
                UpdateQuantity(_currentInstance.quantity);
        }

        if (_currentConso == null || ConsoBarUI.Instance == null) return;
        float remaining = ConsoBarUI.Instance.GetCooldownRemaining(_currentConso);
        float total      = ConsoBarUI.Instance.GetCooldownDuration(_currentConso);
        SetCooldown(remaining, total);
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

        // Seuls Potion/Food/TeleportItem ont un sens dans une barre d'action utilisable en un
        // clic — DungeonKey (passe par le Portal gaté, pas la barre), RewardChest (s'ouvre depuis
        // l'inventaire) et Other (effet non défini) sont exclus.
        ConsumableType type = conso.data.consumableType;
        if (type != ConsumableType.Potion && type != ConsumableType.Food && type != ConsumableType.TeleportItem)
        {
            UnityEngine.Debug.Log($"[CONSO DROP] {conso.Name} ({type}) ne peut pas être placé dans la ConsoBar.");
            return;
        }

        ConsoBarUI.Instance?.AssignConsoInstance(slotIndex, conso);
        UnityEngine.Debug.Log($"[CONSO DROP] {conso.Name} assigné au slot {slotIndex}.");
    }
}