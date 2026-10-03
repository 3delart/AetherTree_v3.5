using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// =============================================================
// SKILLBARUI.CS — Barre de compétences actives (UI)
// Path : Assets/Scripts/UI/SkillBarUI.cs
// AetherTree GDD v30 — Section 8.1
//
// Glisser les 10 GameObjects SkillSlot1…SkillSlot10 dans slots[].
// Les enfants (SkillIcon, CDOverlay, CD, MPCost, KeyBinding)
// sont trouvés automatiquement par nom.
//
// SlotType auto-assigné par index (GDD §8.1) :
//   Slot 0     → SlotType.BasicAttack
//   Slots 1-8  → SlotType.Active
//   Slot 9     → SlotType.Ultimate
// =============================================================

public class SkillBarUI : MonoBehaviour
{
    public static SkillBarUI Instance { get; private set; }

    [Header("Slots — glisser les 10 GameObjects ici")]
    public GameObject[] slots = new GameObject[10];

    private SkillSlotUI[] _slotUIs = new SkillSlotUI[10];
    private SkillBar      _skillBar;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            Transform t = slots[i].transform;

            SkillSlotUI slot = slots[i].GetComponent<SkillSlotUI>();
            if (slot == null) slot = slots[i].AddComponent<SkillSlotUI>();

            slot.skillIcon      = t.Find("SkillIcon") ?.GetComponent<Image>();
            slot.cdOverlay      = t.Find("CDOverlay") ?.GetComponent<Image>();
            slot.cdText         = t.Find("CD")        ?.GetComponent<TextMeshProUGUI>();
            slot.mpCostText     = t.Find("MPCost")    ?.GetComponent<TextMeshProUGUI>();
            slot.keyBindingText = t.Find("KeyBinding")?.GetComponent<TextMeshProUGUI>();
            slot.slotIndex      = i;
            slot.Init();

            // Ajoute TooltipTrigger si absent
            var tooltip = slots[i].GetComponent<TooltipTrigger>();
            if (tooltip == null) tooltip = slots[i].AddComponent<TooltipTrigger>();

            _slotUIs[i] = slot;

            // ── Drop target avec SlotType auto selon index ────
            var drop = slots[i].GetComponent<SkillDropTarget>();
            if (drop == null) drop = slots[i].AddComponent<SkillDropTarget>();
            drop.slotIndex = i;
            drop.slotType  = i == 0 ? SlotType.BasicAttack
                           : i == 9 ? SlotType.Ultimate
                                    : SlotType.Active;

            // Clic → utilise le slot
            var btn = slots[i].GetComponent<Button>();
            if (btn != null)
            {
                int captured = i;
                btn.onClick.AddListener(() => SkillBar.Instance?.TryUseSlot(captured));
            }
        }
    }

    private void Start()
    {
        _skillBar = SkillBar.Instance;
        if (_skillBar == null) Debug.LogWarning("[SkillBarUI] SkillBar.Instance introuvable !");
        RefreshAll();
    }

    private void Update() => RefreshCooldowns();

    public void RefreshAll()
    {
        if (_skillBar == null) return;
        for (int i = 0; i < _slotUIs.Length; i++)
            _slotUIs[i]?.SetSkill(_skillBar.GetSkillAtSlot(i));
    }

    public void RefreshSlot(int i)
    {
        if (_skillBar == null || i < 0 || i >= _slotUIs.Length) return;
        _slotUIs[i]?.SetSkill(_skillBar.GetSkillAtSlot(i));
    }

    /// <summary>
    /// Affiche temporairement un skill différent dans un slot (combo séquentiel).
    /// Montre le prochain step du combo sans modifier _slots[].
    /// Appelé par SkillBar.TryAdvanceCombo() pour chaque step intermédiaire.
    /// </summary>
    public void RefreshSlotWithSkill(int i, SkillData skill)
    {
        if (i < 0 || i >= _slotUIs.Length) return;
        _slotUIs[i]?.SetSkill(skill);
    }

    public void OnSkillUsed(int i)
    {
        if (i >= 0 && i < _slotUIs.Length) _slotUIs[i]?.PlayUsedFeedback();
    }

    private void RefreshCooldowns()
    {
        if (_skillBar == null) return;
        for (int i = 0; i < _slotUIs.Length; i++)
            _slotUIs[i]?.SetCooldown(_skillBar.GetCooldownRemaining(i), _skillBar.GetCooldownTotal(i));
    }
}

// =============================================================
// SKILLSLOTUI — attaché automatiquement sur chaque slot
// =============================================================
public class SkillSlotUI : MonoBehaviour, IPointerClickHandler
{
    [HideInInspector] public Image           skillIcon;
    [HideInInspector] public Image           cdOverlay;
    [HideInInspector] public TextMeshProUGUI cdText;
    [HideInInspector] public TextMeshProUGUI mpCostText;
    [HideInInspector] public TextMeshProUGUI keyBindingText;
    [HideInInspector] public int             slotIndex = -1;

    private SkillData _currentSkill;

    /// <summary>Clic droit — vide le slot. Exige la SkillLibrary ouverte : c'est le geste qui
    /// rend le clic droit VOLONTAIRE (on ne vide jamais un slot par un clic droit machinal en
    /// plein combat). Slot 0 (attaque de base) et 9 (ultime) restent vidables comme les autres —
    /// pas d'exception demandée.</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right) return;
        if (_currentSkill == null || slotIndex < 0) return;
        if (SkillLibraryUI.Instance == null || !SkillLibraryUI.Instance.IsOpen) return;
        SkillBar.Instance?.SetSkillAtSlot(slotIndex, null);
    }

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

    public void SetSkill(SkillData skill)
    {
        _currentSkill = skill;
        if (skillIcon != null)
        {
            if (skill == null || skill.icon == null)
            {
                skillIcon.sprite  = null;
                skillIcon.color   = EmptyColor;
                skillIcon.enabled = false;
            }
            else
            {
                skillIcon.sprite  = skill.icon;
                skillIcon.color   = Color.white;
                skillIcon.enabled = true;
            }
        }
        if (mpCostText != null)
            mpCostText.text = skill != null && skill.manaCost > 0 ? $"{skill.manaCost}" : "";

        // Tooltip
        GetComponent<TooltipTrigger>()?.SetSkill(skill);
    }

    public void SetCooldown(float remaining, float total)
    {
        bool onCD = remaining > 0f && total > 0f;
        if (cdOverlay != null)
        {
            cdOverlay.gameObject.SetActive(onCD);
            cdOverlay.fillAmount = onCD ? remaining / total : 0f;
            cdOverlay.color      = onCD ? OnCooldown : Color.clear;
        }
        if (cdText != null)
        {
            cdText.gameObject.SetActive(onCD);
            cdText.text = onCD ? Mathf.CeilToInt(remaining).ToString() : "";
        }
        if (skillIcon != null && _currentSkill != null)
            skillIcon.color = onCD ? DimmedColor : Color.white;
    }

    public void PlayUsedFeedback() => StartCoroutine(FlashRoutine());
    private System.Collections.IEnumerator FlashRoutine()
    {
        if (skillIcon == null) yield break;
        skillIcon.color = Color.yellow;
        yield return new WaitForSeconds(0.08f);
        skillIcon.color = Color.white;
    }
}
