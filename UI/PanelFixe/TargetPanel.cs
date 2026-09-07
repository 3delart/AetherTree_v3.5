using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// =============================================================
// TARGETPANEL.CS — Panel secondaire — Affichage de la cible
// AetherTree GDD v21 — v3.1 : ajout ShowNode(ResourceNode)
// =============================================================

public class TargetPanel : MonoBehaviour, IPointerClickHandler
{
    public static TargetPanel Instance { get; private set; }

    [Header("Identité")]
    public Image           targetIcon;
    public TextMeshProUGUI targetNameText;
    public TextMeshProUGUI targetLevelText;

    [Header("Barres")]
    public Slider          hpBar;
    public Slider          mpBar;
    [Tooltip("Slider superposé à hpBar — Direction = Right To Left dans son propre Inspector.\nmaxValue = MaxHP, value = shield actif, recouvre la barre HP verte au prorata.")]
    public Slider          shieldBar;

    [Header("Effets (container HorizontalLayoutGroup)")]
    public Transform       statusEffectContainer;
    public GameObject      effectIconPrefab;

    // ── Runtime ──────────────────────────────────────────────
    private Entity       _currentTarget;
    private ResourceNode _currentNode;   // ← nouveau
    private Dictionary<string, StatusEffectIcon> _activeIcons = new Dictionary<string, StatusEffectIcon>();

    // ─────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        gameObject.SetActive(false);
    }

    private void Update()
    {
        // Node sélectionné — pas de barres à mettre à jour
        if (_currentNode != null) return;

        if (_currentTarget == null || _currentTarget.isDead)
        {
            ClearTarget();
            return;
        }
        RefreshBars();
        RefreshEffects();
    }

    // =========================================================
    // API — ENTITY
    // =========================================================

    public void SetTarget(Entity target)
    {
        ClearNodeDisplay();
        foreach (var kvp in _activeIcons) Destroy(kvp.Value.gameObject);
        _activeIcons.Clear();

        _currentTarget = target;

        if (target == null) { gameObject.SetActive(false); return; }

        gameObject.SetActive(true);
        RefreshIdentity();
        RefreshBars();
        RefreshEffects();
    }

    public void ClearTarget()
    {
        _currentTarget = null;
        _currentNode   = null;
        foreach (var kvp in _activeIcons) Destroy(kvp.Value.gameObject);
        _activeIcons.Clear();
        gameObject.SetActive(false);
    }

    public void Show(Entity target) => SetTarget(target);
    public void Hide()              => ClearTarget();

    // =========================================================
    // API — RESOURCE NODE (nouveau v3.1)
    // =========================================================

    /// <summary>Affiche les infos d'un ResourceNode dans le TargetPanel.</summary>
    public void ShowNode(ResourceNode node)
    {
        if (node == null) { ClearTarget(); return; }

        // Cache les barres HP/MP — pas pertinentes pour un node
        ClearEntityDisplay();
        _currentNode   = node;
        _currentTarget = null;

        gameObject.SetActive(true);

        if (targetNameText  != null)
            targetNameText.text  = node.data?.displayName.Get(LocalizationManager.CurrentLanguage) ?? "Ressource";

        if (targetLevelText != null)
            targetLevelText.text = node.data?.resourceType.ToString() ?? "";

        if (targetIcon != null)
        {
            targetIcon.sprite = node.data?.icon;
            targetIcon.color  = node.data?.icon != null
                ? Color.white
                : new Color(0.6f, 0.85f, 0.3f, 1f); // vert si pas d'icône
        }

        // Cache les barres HP/MP
        if (hpBar != null) hpBar.gameObject.SetActive(false);
        if (mpBar != null) mpBar.gameObject.SetActive(false);
        if (shieldBar != null) shieldBar.gameObject.SetActive(false);
    }

    // =========================================================
    // REFRESH — ENTITY
    // =========================================================

    private void RefreshIdentity()
    {
        if (_currentTarget == null) return;

        if (targetNameText != null)
            targetNameText.text = _currentTarget.entityName;

        Mob    mob    = _currentTarget as Mob;
        Player player = _currentTarget as Player;
        PNJ    pnj    = _currentTarget as PNJ;

        if (targetLevelText != null)
        {
            if      (mob    != null) targetLevelText.text = $"Niv. {mob.mobLevel}";
            else if (player != null) targetLevelText.text = $"Niv. {player.level}";
            else                     targetLevelText.text = "";
        }

        if (targetIcon != null)
        {
            targetIcon.sprite = null;
            targetIcon.color  = new Color(0.3f, 0.3f, 0.3f, 1f);

            if      (mob    != null && mob.data?.portrait    != null) { targetIcon.sprite = mob.data.portrait;    targetIcon.color = Color.white; }
            else if (player != null && player.characterData?.portrait != null) { targetIcon.sprite = player.characterData.portrait; targetIcon.color = Color.white; }
            else if (pnj    != null && pnj.data?.portrait    != null) { targetIcon.sprite = pnj.data.portrait;    targetIcon.color = Color.white; }
        }

        // Réaffiche les barres HP/MP pour les entités
        if (hpBar != null) hpBar.gameObject.SetActive(true);
        if (mpBar != null) mpBar.gameObject.SetActive(true);
    }

    private void RefreshBars()
    {
        if (_currentTarget == null) return;
        if (hpBar != null) { hpBar.maxValue = _currentTarget.MaxHP;   hpBar.value = _currentTarget.CurrentHP; }
        if (mpBar != null) { mpBar.maxValue = _currentTarget.MaxMana; mpBar.value = _currentTarget.CurrentMana; }

        // Shield actif — même overlay que PlayerInfosPanel (maxValue = MaxHP, pas CurrentHP).
        if (shieldBar != null)
        {
            float shield = _currentTarget.statusEffects != null ? _currentTarget.statusEffects.GetActiveShieldAmount() : 0f;
            shieldBar.maxValue = _currentTarget.MaxHP;
            shieldBar.value    = shield;
            shieldBar.gameObject.SetActive(shield > 0f);
        }
    }

    private void RefreshEffects()
    {
        if (statusEffectContainer == null || effectIconPrefab == null) return;
        if (_currentTarget?.statusEffects == null) return;

        var effects = _currentTarget.statusEffects.GetActiveEffectsForUI();

        foreach (var entry in effects)
        {
            if (!_activeIcons.ContainsKey(entry.key))
            {
                var go   = Instantiate(effectIconPrefab, statusEffectContainer);
                go.name  = entry.key;
                var icon = go.GetComponent<StatusEffectIcon>() ?? go.AddComponent<StatusEffectIcon>();
                icon.Setup(entry, entry.isDebuff
                    ? new UnityEngine.Color(0.8f, 0.1f, 0.1f)
                    : new UnityEngine.Color(0.1f, 0.8f, 0.2f), 28f);
                _activeIcons[entry.key] = icon;
            }
            else
                _activeIcons[entry.key].UpdateDisplay(entry.remainingTime, entry.totalDuration);
        }

        var toRemove = new List<string>();
        foreach (var kvp in _activeIcons)
            if (!effects.Exists(e => e.key == kvp.Key)) toRemove.Add(kvp.Key);
        foreach (var key in toRemove) { Destroy(_activeIcons[key].gameObject); _activeIcons.Remove(key); }
    }

    // =========================================================
    // HELPERS
    // =========================================================

    private void ClearNodeDisplay()
    {
        _currentNode = null;
        if (hpBar != null) hpBar.gameObject.SetActive(true);
        if (mpBar != null) mpBar.gameObject.SetActive(true);
    }

    private void ClearEntityDisplay()
    {
        foreach (var kvp in _activeIcons) Destroy(kvp.Value.gameObject);
        _activeIcons.Clear();
    }

    // =========================================================
    // CLIC DROIT
    // =========================================================

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right) return;
        if (_currentTarget == null && _currentNode == null) return;
        Debug.Log($"[TargetPanel] Clic droit → DetailWindow (non implémenté)");
    }
}
