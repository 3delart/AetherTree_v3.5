using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using TMPro;

// =============================================================
// StatusEffectUI — Affiche les icônes des buffs/debuffs actifs
// AetherTree GDD v21
//
// Setup :
//   1. Créer un GameObject "StatusEffectUI" enfant du HUD
//   2. Positionner en dessous des barres HP/MP
//   3. Attacher ce script
//   4. Assigner statusEffectSystem (Player) et iconPrefab
//   5. Assigner le container (HorizontalLayoutGroup)
//
// IconPrefab requis :
//   - Image (icône)
//   - Image enfant "Cooldown" (fill radial pour le timer)
//   - TextMeshProUGUI enfant "Timer" (texte durée restante)
// =============================================================

public class StatusEffectUI : MonoBehaviour
{
    [Header("Références")]
    [Tooltip("StatusEffectSystem du joueur.")]
    public StatusEffectSystem statusEffectSystem;

    [Tooltip("Prefab d'une icône d'effet.\nDoit contenir : Image (icône) + Image 'Cooldown' (fill) + TMP 'Timer'.")]
    public GameObject iconPrefab;

    [Tooltip("Container avec HorizontalLayoutGroup pour aligner les icônes.")]
    public Transform iconContainer;

    [Header("Apparence")]
    [Tooltip("Taille des icônes en pixels.")]
    public float iconSize = 32f;

    [Tooltip("Couleur de bordure pour les debuffs.")]
    public Color debuffBorderColor = new Color(0.8f, 0.1f, 0.1f, 1f);

    [Tooltip("Couleur de bordure pour les buffs.")]
    public Color buffBorderColor = new Color(0.1f, 0.8f, 0.2f, 1f);

    // ── Runtime ───────────────────────────────────────────────
    private Dictionary<string, StatusEffectIcon> _activeIcons
        = new Dictionary<string, StatusEffectIcon>();

    private void Update()
    {
        if (statusEffectSystem == null) return;
        RefreshIcons();
    }

    // =========================================================
    // REFRESH — synchronise les icônes avec les effets actifs
    // =========================================================

    private void RefreshIcons()
    {
        var currentEffects = statusEffectSystem.GetActiveEffectsForUI();

        // Ajouter les nouveaux effets
        foreach (var entry in currentEffects)
        {
            if (!_activeIcons.ContainsKey(entry.key))
                AddIcon(entry);
        }

        // Mettre à jour ou supprimer les icônes existantes
        var toRemove = new List<string>();
        foreach (var kvp in _activeIcons)
        {
            var entry = currentEffects.Find(e => e.key == kvp.Key);
            if (entry == null)
                toRemove.Add(kvp.Key);
            else
                kvp.Value.UpdateDisplay(entry.remainingTime, entry.totalDuration);
        }

        foreach (var key in toRemove)
        {
            Destroy(_activeIcons[key].gameObject);
            _activeIcons.Remove(key);
        }
    }

    private void AddIcon(StatusEffectUIEntry entry)
    {
        if (iconPrefab == null || iconContainer == null) return;

        GameObject go = Instantiate(iconPrefab, iconContainer);
        go.name = $"Icon_{entry.key}";

        var icon = go.GetComponent<StatusEffectIcon>();
        if (icon == null) icon = go.AddComponent<StatusEffectIcon>();

        icon.Setup(entry, entry.isDebuff ? debuffBorderColor : buffBorderColor, iconSize);
        _activeIcons[entry.key] = icon;
    }
}

// =============================================================
// StatusEffectUIEntry — données passées à l'UI
// =============================================================
public class StatusEffectUIEntry
{
    public string           key;            // DebuffType ou BuffType en string (clé unique)
    public Sprite           icon;
    public float            remainingTime;
    public float            totalDuration;
    public bool             isDebuff;
    public StatusEffectData data;           // référence au SO pour le tooltip
}

// =============================================================
// StatusEffectIcon — gère l'affichage d'une icône individuelle
// =============================================================
public class StatusEffectIcon : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private StatusEffectUIEntry _entry;
    private Image               _iconImage;
    private Image               _cooldownFill;
    private TMP_Text            _timerText;
    private Image               _border;

    // =========================================================
    // SETUP
    // =========================================================

    public void Setup(StatusEffectUIEntry entry, Color borderColor, float size)
    {
        _entry = entry;

        // Taille
        var rect = GetComponent<RectTransform>();
        if (rect != null) rect.sizeDelta = new Vector2(size, size);

        // Icône
        _iconImage = GetComponent<Image>();
        if (_iconImage != null && entry.icon != null)
            _iconImage.sprite = entry.icon;

        // Fill radial timer
        _cooldownFill = transform.Find("Cooldown")?.GetComponent<Image>();
        if (_cooldownFill != null)
        {
            _cooldownFill.type        = Image.Type.Filled;
            _cooldownFill.fillMethod  = Image.FillMethod.Radial360;
            _cooldownFill.fillAmount  = 1f;
        }

        // Texte timer
        _timerText = transform.Find("Timer")?.GetComponent<TMP_Text>();

        // Bordure couleur buff/debuff
        _border = transform.Find("Border")?.GetComponent<Image>();
        if (_border != null) _border.color = borderColor;

        UpdateDisplay(entry.remainingTime, entry.totalDuration);
    }

    // =========================================================
    // UPDATE DISPLAY
    // =========================================================

    // Durée "énorme" = permanent en pratique (convention du projet pour les effets sans notion
    // d'expiration naturelle, ex: debuffs Aura) — voir même garde-fou dans TooltipSystem.
    // ShowStatusEffectTooltip. Sans lui, l'icône afficherait un texte du genre "999998.2s" qui
    // change chaque frame, sur un effet censé paraître stable/permanent.
    private const float PermanentThreshold = 100000f;

    public void UpdateDisplay(float remaining, float total)
    {
        if (_entry != null)
        {
            _entry.remainingTime = remaining;
            _entry.totalDuration = total;
        }

        bool isPermanent = total >= PermanentThreshold;

        if (_cooldownFill != null)
            _cooldownFill.fillAmount = isPermanent ? 1f : (total > 0f ? remaining / total : 0f);

        if (_timerText != null)
            _timerText.text = isPermanent ? "" : (remaining > 0f ? $"{remaining:F1}s" : "");
    }

    // =========================================================
    // TOOLTIP
    // =========================================================

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_entry == null) return;
        TooltipSystem.Instance?.ShowStatusEffectTooltip(_entry);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        TooltipSystem.Instance?.HideTooltip();
    }
}
