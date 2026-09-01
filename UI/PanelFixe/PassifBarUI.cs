using UnityEngine;
using UnityEngine.UI;
using TMPro;

// =============================================================
// PASSIFBARUI.CS — Barre des 3 passifs équipés (PassiveSkillData)
// AetherTree GDD v30 — §7.5
//
// Glisser les 3 GameObjects PassifSlot1…3 dans slots[].
// Les enfants (PassifIcon, CDOverlay, CD) trouvés par nom.
//
// Chaque slot reçoit un SkillDropTarget avec SlotType.Passive.
// Drag depuis SkillLibrary (onglet Passifs) → slot via SkillDragDrop
// (PassiveDragSource). Source de vérité = Player.equippedPassives — cette
// classe ne fait que refléter/écrire dedans, jamais son propre état local.
// =============================================================

public class PassifBarUI : MonoBehaviour
{
    public static PassifBarUI Instance { get; private set; }

    [Header("Slots — glisser les 3 GameObjects ici")]
    public GameObject[] slots = new GameObject[3];

    private PassifSlotBarUI[] _slotUIs = new PassifSlotBarUI[3];
    private Player            _player;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            Transform t = slots[i].transform;

            PassifSlotBarUI slot = slots[i].GetComponent<PassifSlotBarUI>();
            if (slot == null) slot = slots[i].AddComponent<PassifSlotBarUI>();

            slot.passifIcon = t.Find("PassifIcon")?.GetComponent<Image>();
            slot.cdOverlay  = t.Find("CDOverlay") ?.GetComponent<Image>();
            slot.cdText     = t.Find("CD")        ?.GetComponent<TextMeshProUGUI>();
            slot.Init();

            _slotUIs[i] = slot;

            // ── Drop target — SlotType.Passive ────────────────
            var drop = slots[i].GetComponent<SkillDropTarget>();
            if (drop == null) drop = slots[i].AddComponent<SkillDropTarget>();
            drop.slotIndex = i;
            drop.slotType  = SlotType.Passive;
        }
    }

    private void Start()
    {
        _player = FindObjectOfType<Player>();
        RefreshAll();
    }

    // =========================================================
    // API PUBLIQUE
    // =========================================================

    /// <summary>
    /// Équipe un passif dans un slot — écrit directement dans
    /// Player.equippedPassives (source de vérité, lue par PassiveSkillSystem).
    /// Appelé par SkillDropTarget.OnDrop().
    /// </summary>
    public void SetPassifAtSlot(int index, PassiveSkillData passive)
    {
        if (_player == null) _player = FindObjectOfType<Player>();
        if (_player == null || index < 0 || index >= 3) return;

        _player.equippedPassives[index] = passive;
        _slotUIs[index]?.SetPassif(passive);
        Debug.Log($"[PASSIF] Slot {index} → {passive?.name ?? "vide"}");
    }

    public PassiveSkillData GetPassifAtSlot(int index)
    {
        if (_player?.equippedPassives == null || index < 0 || index >= 3) return null;
        return _player.equippedPassives[index];
    }

    public void RefreshAll()
    {
        if (_player == null) _player = FindObjectOfType<Player>();
        if (_player?.equippedPassives == null) return;

        for (int i = 0; i < _slotUIs.Length; i++)
            _slotUIs[i]?.SetPassif(_player.equippedPassives[i]);
    }

    public void RefreshSlot(int index, PassiveSkillData passive)
    {
        if (index >= 0 && index < _slotUIs.Length)
            _slotUIs[index]?.SetPassif(passive);
    }
}

// =============================================================
// PASSIFSLOTBARUI — attaché automatiquement sur chaque slot
// =============================================================
public class PassifSlotBarUI : MonoBehaviour
{
    [HideInInspector] public Image           passifIcon;
    [HideInInspector] public Image           cdOverlay;
    [HideInInspector] public TextMeshProUGUI cdText;

    private static readonly Color EmptyColor = new Color(0f, 0f, 0f, 0.4f);

    public void Init()
    {
        if (cdOverlay == null) return;
        cdOverlay.type       = Image.Type.Filled;
        cdOverlay.fillMethod = Image.FillMethod.Radial360;
        cdOverlay.fillAmount = 0f;
        cdOverlay.gameObject.SetActive(false);
    }

    public void SetPassif(PassiveSkillData passive)
    {
        if (passifIcon == null) return;
        if (passive == null || passive.icon == null)
        {
            passifIcon.sprite  = null;
            passifIcon.color   = EmptyColor;
            passifIcon.enabled = false;
        }
        else
        {
            passifIcon.sprite  = passive.icon;
            passifIcon.color   = Color.white;
            passifIcon.enabled = true;
        }
    }

    public void SetCooldown(float remaining, float total)
    {
        bool onCD = remaining > 0f && total > 0f;
        if (cdOverlay != null) { cdOverlay.gameObject.SetActive(onCD); cdOverlay.fillAmount = onCD ? remaining / total : 0f; }
        if (cdText    != null) { cdText.gameObject.SetActive(onCD);    cdText.text = onCD ? Mathf.CeilToInt(remaining).ToString() : ""; }
    }
}
