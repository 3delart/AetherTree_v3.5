using UnityEngine;
using UnityEngine.EventSystems;

// =============================================================
// TOOLTIPTRIGGER — Composant à ajouter sur chaque élément survolable
// Path : Assets/Scripts/UI/TooltipTrigger.cs
// AetherTree GDD v30
//
// Usage :
//   1. Ajoute ce composant sur InventoryItemCell, EquipmentSlotHandler,
//      SkillDropTarget, ou tout autre GO survolable.
//   2. Assigne soit _item soit _skill selon le contexte.
//   3. Pour les cellules dynamiques (InventoryItemCell), appelle
//      SetItem(item) / SetSkill(skill) depuis le code.
//
// Note : InventoryItemCell, EquipmentSlotHandler et SkillDropTarget
// implémentent déjà IPointerEnterHandler / IPointerExitHandler.
// Ce composant peut cohabiter avec eux sans conflit.
// =============================================================

public class TooltipTrigger : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler
{
    // ── Données à afficher ────────────────────────────────────
    private InventoryItem      _item;
    private SkillData          _skill;
    private PermanentSkillData _permanentSkill;
    private PassiveSkillData   _passiveSkill;

    [Header("Délai avant affichage (secondes)")]
    public float hoverDelay = 0.4f;

    private float   _hoverTimer = 0f;
    private bool    _hovering   = false;
    private bool    _shown      = false;

    // =========================================================
    // API PUBLIQUE — assignation dynamique
    // =========================================================

    public void SetItem(InventoryItem item)
    {
        _item           = item;
        _skill          = null;
        _permanentSkill = null;
        _passiveSkill   = null;
    }

    public void SetSkill(SkillData skill)
    {
        _skill          = skill;
        _item           = null;
        _permanentSkill = null;
        _passiveSkill   = null;
    }

    public void SetPermanentSkill(PermanentSkillData permanent)
    {
        _permanentSkill = permanent;
        _item           = null;
        _skill          = null;
        _passiveSkill   = null;
    }

    public void SetPassiveSkill(PassiveSkillData passive)
    {
        _passiveSkill   = passive;
        _item           = null;
        _skill          = null;
        _permanentSkill = null;
    }

    public void Clear()
    {
        _item           = null;
        _skill          = null;
        _permanentSkill = null;
        _passiveSkill   = null;
        HideIfShown();
    }

    // =========================================================
    // HOVER
    // =========================================================

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_item == null && _skill == null && _permanentSkill == null && _passiveSkill == null) return;
        _hovering   = true;
        _hoverTimer = 0f;
        _shown      = false;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovering = false;
        HideIfShown();
    }

    private void Update()
    {
        if (!_hovering || _shown) return;

        _hoverTimer += Time.unscaledDeltaTime;
        if (_hoverTimer >= hoverDelay)
        {
            ShowTooltip();
            _shown = true;
        }
    }

    private void OnDisable()
    {
        _hovering = false;
        HideIfShown();
    }

    // =========================================================
    // AFFICHAGE
    // =========================================================

    private void ShowTooltip()
    {
        if (TooltipSystem.Instance == null) return;

        if (_item  != null) TooltipSystem.Instance.ShowItemTooltip(_item);
        else if (_skill != null) TooltipSystem.Instance.ShowSkillTooltip(_skill);
        else if (_permanentSkill != null) TooltipSystem.Instance.ShowPermanentSkillTooltip(_permanentSkill);
        else if (_passiveSkill != null) TooltipSystem.Instance.ShowPassiveSkillTooltip(_passiveSkill);
    }

    private void HideIfShown()
    {
        if (_shown)
        {
            TooltipSystem.Instance?.HideTooltip();
            _shown = false;
        }
    }
}
