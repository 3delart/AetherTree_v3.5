using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// =============================================================
// PLAYERINFOSPANEL.CS — Panel fixe haut gauche
// AetherTree GDD v21
// =============================================================
// SETUP HIERARCHY :
//   PlayerInfosPanel
//     ├── PlayerNamePanel
//     │     ├── PlayerName       (TMP)
//     │     ├── PlayerLevel      (TMP)
//     │     └── PlayerTitle      (TMP)
//     ├── PlayerHPBar            (Slider)
//     │     └── PlayerHPValue    (TMP)
//     ├── PlayerMPBar            (Slider)
//     │     └── PlayerMPValue    (TMP)
//     ├── PlayerIconPanel
//     │     └── PlayerImage      (Image)
//     └── PlayerEffectPanel      (Transform — icônes buffs/debuffs)
//
// CLIC DROIT → ouvre PlayerDetailWindow via DetailWindowManager
// Toutes les données sont lues en Update() — pas d'events requis.
// La fenêtre détail se rafraîchit à l'ouverture uniquement.
// =============================================================

public class PlayerInfosPanel : MonoBehaviour, IPointerClickHandler
{
    public static PlayerInfosPanel Instance { get; private set; }

    // ── Références UI ─────────────────────────────────────────
    [Header("Identité")]
    public TextMeshProUGUI playerNameText;
    public TextMeshProUGUI playerLevelText;
    public TextMeshProUGUI playerTitleText;
    public Image           playerIcon;

    [Header("HP")]
    public Slider          hpBar;
    public TextMeshProUGUI hpValueText;     // "450 / 600"

    [Header("MP")]
    public Slider          mpBar;
    public TextMeshProUGUI mpValueText;     // "120 / 300"

    [Header("Effets (container HorizontalLayoutGroup)")]
    public Transform       effectContainer;
    [Tooltip("Prefab icône effet — Image + enfants 'Cooldown' (Image fill) + 'Timer' (TMP)")]
    public GameObject      effectIconPrefab;

    // ── Cache status effects ──────────────────────────────────
    private StatusEffectSystem _statusEffects;
    private Dictionary<string, StatusEffectIcon> _activeIcons = new Dictionary<string, StatusEffectIcon>();

    // ── Runtime ──────────────────────────────────────────────
    private Player _player;

    // Cache — évite de recréer les strings inutilement
    private int    _cachedLevel  = -1;
    private string _cachedTitle  = null;
    private string _cachedName   = null;

    // ─────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _player = FindObjectOfType<Player>();
        if (_player == null)
        {
            Debug.LogWarning("[PlayerInfosPanel] Player introuvable !");
            return;
        }

        // Attend une frame que Player.Awake() + RecalculateStats() soient terminés
        StartCoroutine(InitNextFrame());
    }

    private System.Collections.IEnumerator InitNextFrame()
    {
        yield return null;
        _statusEffects = _player.statusEffects;
        RefreshIcon();
        ForceRefreshAll();
    }

    // ── Update — rafraîchit tout en temps réel ───────────────
    private void Update()
    {
        if (_player == null) return;

        RefreshBars();
        RefreshIdentityIfChanged();
        RefreshEffects();
    }

    // =========================================================
    // REFRESH
    // =========================================================

    /// <summary>Identité — ne met à jour les textes que si les valeurs ont changé.</summary>
    private void RefreshIdentityIfChanged()
    {
        if (_player.entityName != _cachedName)
        {
            _cachedName = _player.entityName;
            if (playerNameText != null) playerNameText.text = _cachedName;
        }

        if (_player.level != _cachedLevel)
        {
            _cachedLevel = _player.level;
            if (playerLevelText != null) playerLevelText.text = $"Niv. {_cachedLevel}";
        }

        if (_player.activeTitle != _cachedTitle)
        {
            _cachedTitle = _player.activeTitle;
            if (playerTitleText != null) playerTitleText.text = _cachedTitle;
        }
    }

    private void RefreshBars()
    {
        // HP
        if (hpBar != null)
        {
            hpBar.maxValue = _player.MaxHP;
            hpBar.value    = _player.CurrentHP;
        }
        if (hpValueText != null)
            hpValueText.text = $"{Mathf.CeilToInt(_player.CurrentHP)} / {Mathf.CeilToInt(_player.MaxHP)}";

        // MP
        if (mpBar != null)
        {
            mpBar.maxValue = _player.MaxMana;
            mpBar.value    = _player.CurrentMana;
        }
        if (mpValueText != null)
            mpValueText.text = $"{Mathf.CeilToInt(_player.CurrentMana)} / {Mathf.CeilToInt(_player.MaxMana)}";
    }

    private void RefreshIcon()
    {
        // L'icône est définie sur le CharacterData — chargée une seule fois
        if (playerIcon != null && _player.characterData != null && _player.characterData.portrait != null)
            playerIcon.sprite = _player.characterData.portrait;
    }

    /// <summary>Force un rafraîchissement complet — appelé au Start et si besoin depuis l'extérieur.</summary>
    public void ForceRefreshAll()
    {
        _cachedName  = null;
        _cachedLevel = -1;
        _cachedTitle = null;
        RefreshIdentityIfChanged();
        RefreshBars();
    }

    // =========================================================
    // STATUS EFFECTS
    // =========================================================

    private void RefreshEffects()
    {
        if (effectContainer == null || effectIconPrefab == null || _statusEffects == null) return;

        var effects = _statusEffects.GetActiveEffectsForUI();

        // Ajouter les nouveaux effets
        foreach (var entry in effects)
        {
            if (!_activeIcons.ContainsKey(entry.key))
            {
                var go = Instantiate(effectIconPrefab, effectContainer);
                go.name = entry.key;
                var icon = go.GetComponent<StatusEffectIcon>() ?? go.AddComponent<StatusEffectIcon>();
                icon.Setup(entry, entry.isDebuff
                    ? new UnityEngine.Color(0.8f, 0.1f, 0.1f)
                    : new UnityEngine.Color(0.1f, 0.8f, 0.2f), 32f);
                _activeIcons[entry.key] = icon;
            }
            else
            {
                _activeIcons[entry.key].UpdateDisplay(entry.remainingTime, entry.totalDuration);
            }
        }

        // Supprimer les effets expirés
        var toRemove = new List<string>();
        foreach (var kvp in _activeIcons)
            if (!effects.Exists(e => e.key == kvp.Key)) toRemove.Add(kvp.Key);

        foreach (var key in toRemove)
        {
            Destroy(_activeIcons[key].gameObject);
            _activeIcons.Remove(key);
        }
    }

    // =========================================================
    // CLIC DROIT — ouvre PlayerDetailWindow
    // La fenêtre lit les données à l'ouverture uniquement.
    // =========================================================

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right) return;

        // TODO : DetailWindowManager.Instance.OpenPlayerDetail(_player);
        Debug.Log("[PlayerInfosPanel] Clic droit → PlayerDetailWindow (non implémenté)");
    }
}