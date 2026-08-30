using UnityEngine;
using UnityEngine.UI;
using TMPro;

// =============================================================
// PASSIFBARUI.CS — Barre des 3 passifs utilitaires
// AetherTree GDD v30
//
// Glisser les 3 GameObjects PassifSlot1…3 dans slots[].
// Les enfants (PassifIcon, CDOverlay, CD) trouvés par nom.
//
// Chaque slot reçoit un SkillDropTarget avec SlotType.PassiveUtility.
// Drag depuis SkillLibrary → slot passif via SkillDragDrop.
// =============================================================

public class PassifBarUI : MonoBehaviour
{
    public static PassifBarUI Instance { get; private set; }

    [Header("Slots — glisser les 3 GameObjects ici")]
    public GameObject[] slots = new GameObject[3];

    private PassifSlotBarUI[] _slotUIs   = new PassifSlotBarUI[3];
    private SkillData[]       _skills    = new SkillData[3];

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

            // ── Drop target — SlotType.PassiveUtility ─────────
            var drop = slots[i].GetComponent<SkillDropTarget>();
            if (drop == null) drop = slots[i].AddComponent<SkillDropTarget>();
            drop.slotIndex = i;
            drop.slotType  = SlotType.PassiveUtility;
        }
    }

    private void Start() => RefreshAll();

    // =========================================================
    // API PUBLIQUE
    // =========================================================

    /// <summary>
    /// Assigne un skill PassiveUtility à un slot.
    /// Appelé par SkillDropTarget.OnDrop().
    /// </summary>
    public void SetPassifAtSlot(int index, SkillData skill)
    {
        if (index < 0 || index >= _slotUIs.Length) return;
        _skills[index] = skill;
        _slotUIs[index]?.SetPassif(skill);
        Debug.Log($"[PASSIF] Slot {index} → {skill?.name ?? "vide"}");
    }

    public SkillData GetPassifAtSlot(int index)
    {
        if (index < 0 || index >= _skills.Length) return null;
        return _skills[index];
    }

    public void RefreshAll()
    {
        for (int i = 0; i < _slotUIs.Length; i++)
            _slotUIs[i]?.SetPassif(_skills[i]);
    }

    public void RefreshSlot(int index, SkillData skill)
    {
        if (index >= 0 && index < _slotUIs.Length)
            _slotUIs[index]?.SetPassif(skill);
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

    public void SetPassif(SkillData skill)
    {
        if (passifIcon == null) return;
        if (skill == null || skill.icon == null)
        {
            passifIcon.sprite  = null;
            passifIcon.color   = EmptyColor;
            passifIcon.enabled = false;
        }
        else
        {
            passifIcon.sprite  = skill.icon;
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
