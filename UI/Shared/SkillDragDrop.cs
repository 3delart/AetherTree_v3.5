using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// =============================================================
// SKILLDRAGDROP.CS — Drag & Drop skill : Library → SkillBar / PassifBar
// Path : Assets/Scripts/UI/SkillDragDrop.cs
// AetherTree GDD v30
//
// Deux composants :
//   SkillDragSource  — sur chaque entrée de SkillLibraryUI
//   SkillDropTarget  — sur chaque slot de SkillBarUI ET PassifBarUI
//
// Compatibilité par skillType (GDD §8.1) :
//   SlotType.BasicAttack    → SkillType.BasicAttack uniquement
//   SlotType.Active         → SkillType.Active uniquement
//   SlotType.Ultimate       → SkillType.Ultimate uniquement
//   SlotType.PassiveUtility → SkillType.PassiveUtility uniquement
//
// Setup automatique :
//   SkillBarUI.Awake()  → SlotType auto selon index (0=BasicAttack, 1-8=Active, 9=Ultimate)
//   PassifBarUI.Awake() → SlotType.PassiveUtility sur chaque slot
// =============================================================

// =============================================================
// SLOT TYPE — détermine ce qu'un slot accepte
// =============================================================
public enum SlotType
{
    BasicAttack,    // Slot 0 SkillBar
    Active,         // Slots 1-8 SkillBar
    Ultimate,       // Slot 9 SkillBar
    PassiveUtility, // Slots P1/P2/P3 PassifBar
}

// =============================================================
// SKILLDRAGSOURCE
// =============================================================
public class SkillDragSource : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [HideInInspector] public SkillData skill;

    private static GameObject _ghost;
    private static SkillData  _dragging;

    private Canvas _rootCanvas;

    /// <summary>
    /// Remonte la hiérarchie pour trouver le Canvas racine.
    /// Appelé à OnBeginDrag — pas dans Awake — pour garantir que
    /// le GameObject est déjà attaché à la hiérarchie du Canvas.
    /// </summary>
    private Canvas FindRootCanvas()
    {
        Canvas[] canvases = GetComponentsInParent<Canvas>(includeInactive: true);
        if (canvases != null && canvases.Length > 0)
            return canvases[canvases.Length - 1];
        return FindObjectOfType<Canvas>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (skill == null) return;
        _dragging = skill;

        // Résout le Canvas racine ici — le GO est garanti dans la hiérarchie.
        _rootCanvas = FindRootCanvas();

        _ghost = new GameObject("SkillDragGhost");
        _ghost.transform.SetParent(_rootCanvas.transform, false);
        _ghost.transform.SetAsLastSibling();

        var rt = _ghost.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(48f, 48f);
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);

        var img = _ghost.AddComponent<Image>();
        img.sprite        = skill.icon;
        img.color         = skill.icon != null ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.8f);
        img.raycastTarget = false;

        MoveGhost(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        MoveGhost(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _dragging = null;
        if (_ghost != null) { Destroy(_ghost); _ghost = null; }
    }

    private void MoveGhost(PointerEventData eventData)
    {
        if (_ghost == null || _rootCanvas == null) return;

        Camera cam = _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : _rootCanvas.worldCamera;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rootCanvas.transform as RectTransform,
            eventData.position,
            cam,
            out Vector2 localPoint);

        (_ghost.transform as RectTransform).anchoredPosition = localPoint;
    }

    public static SkillData CurrentDragging => _dragging;
}

// =============================================================
// SKILLDROP TARGET
// =============================================================
public class SkillDropTarget : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    [HideInInspector] public int      slotIndex = -1;
    [HideInInspector] public SlotType slotType  = SlotType.Active;

    private Image _slotImage;
    private Color _originalColor;

    private static readonly Color HighlightValid   = new Color(0.4f, 0.9f, 0.4f, 0.6f);
    private static readonly Color HighlightInvalid = new Color(0.9f, 0.3f, 0.3f, 0.6f);

    private void Awake()
    {
        _slotImage = GetComponent<Image>();
        if (_slotImage != null) _originalColor = _slotImage.color;
    }

    // ── Compatibilité slot / skill ─────────────────────────────
    /// <summary>
    /// Basé uniquement sur SkillType — pas sur les tags.
    /// </summary>
    private bool IsCompatible(SkillData skill)
    {
        if (skill == null) return false;
        return slotType switch
        {
            SlotType.BasicAttack    => skill.skillType == SkillType.BasicAttack,
            SlotType.Active         => skill.skillType == SkillType.Active,
            SlotType.Ultimate       => skill.skillType == SkillType.Ultimate,
            SlotType.PassiveUtility => skill.skillType == SkillType.PassiveUtility,
            _                       => false,
        };
    }

    // ── Drop ──────────────────────────────────────────────────
    public void OnDrop(PointerEventData eventData)
    {
        ResetHighlight();

        SkillData dropped = SkillDragSource.CurrentDragging;
        if (dropped == null || slotIndex < 0) return;

        if (!IsCompatible(dropped))
        {
            Debug.LogWarning($"[DRAG] {dropped.name} ({dropped.skillType}) " +
                             $"incompatible avec slot {slotIndex} ({slotType}).");
            return;
        }

        // ── Anti-doublon : interdit de placer un skill déjà présent dans la SkillBar ──
        if (slotType == SlotType.BasicAttack ||
            slotType == SlotType.Active      ||
            slotType == SlotType.Ultimate)
        {
            if (SkillBar.Instance != null)
            {
                for (int i = 0; i < 10; i++)
                {
                    if (i == slotIndex) continue; // le slot de destination ne compte pas
                    if (SkillBar.Instance.GetSkillAtSlot(i) == dropped)
                    {
                        Debug.LogWarning($"[DRAG] {dropped.name} est déjà équipé en slot {i} — drop annulé.");
                        return;
                    }
                }
            }
        }

        switch (slotType)
        {
            case SlotType.BasicAttack:
            case SlotType.Active:
            case SlotType.Ultimate:
                SkillBar.Instance?.SetSkillAtSlot(slotIndex, dropped);
                break;

            case SlotType.PassiveUtility:
                PassifBarUI.Instance?.SetPassifAtSlot(slotIndex, dropped);
                break;
        }

        Debug.Log($"[DRAG] {dropped.name} → {slotType} slot {slotIndex}");
    }

    // ── Highlight survol ──────────────────────────────────────
    public void OnPointerEnter(PointerEventData eventData)
    {
        SkillData dragging = SkillDragSource.CurrentDragging;
        if (dragging == null) return;

        bool compatible = IsCompatible(dragging);

        // Vérifie aussi le doublon pour que le highlight soit cohérent avec le drop
        if (compatible &&
            (slotType == SlotType.BasicAttack || slotType == SlotType.Active || slotType == SlotType.Ultimate) &&
            SkillBar.Instance != null)
        {
            for (int i = 0; i < 10; i++)
            {
                if (i == slotIndex) continue;
                if (SkillBar.Instance.GetSkillAtSlot(i) == dragging)
                {
                    compatible = false;
                    break;
                }
            }
        }

        if (_slotImage != null)
            _slotImage.color = compatible ? HighlightValid : HighlightInvalid;
    }

    public void OnPointerExit(PointerEventData eventData) => ResetHighlight();

    private void ResetHighlight()
    {
        if (_slotImage != null) _slotImage.color = _originalColor;
    }
}
