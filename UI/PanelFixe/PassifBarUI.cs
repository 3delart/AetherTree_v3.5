using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// =============================================================
// PASSIFBARUI.CS — Barre des 3 passifs équipés (PassiveSkillData)
// AetherTree GDD v30 — §7.5
//
// Glisser les 3 GameObjects PassifSlot1…3 dans slots[].
// Les enfants (PassifIcon, CDOverlay, CD, IncompatibleOverlay) trouvés par nom.
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
            slot.incompatibleOverlay = t.Find("IncompatibleOverlay")?.GetComponent<Image>();
            slot.slotIndex = i;
            slot.Init();

            _slotUIs[i] = slot;

            // ── Drop target — SlotType.Passive ────────────────
            var drop = slots[i].GetComponent<SkillDropTarget>();
            if (drop == null) drop = slots[i].AddComponent<SkillDropTarget>();
            drop.slotIndex = i;
            drop.slotType  = SlotType.Passive;

            // ── Drag source — retour PassifBar → SkillLibrary ────
            slot.dragSource = slots[i].GetComponent<PassiveDragSource>();
            if (slot.dragSource == null) slot.dragSource = slots[i].AddComponent<PassiveDragSource>();

            // Ajoute TooltipTrigger si absent — même pattern que SkillBarUI
            if (slots[i].GetComponent<TooltipTrigger>() == null)
                slots[i].AddComponent<TooltipTrigger>();
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

        // Révoque les buffs actifs accordés par le passif SORTANT de ce slot — sans ça, swap un
        // passif juste après un proc (OnInterval en particulier, dont le CD tourne en
        // permanence donc ce moment arrive tout le temps) permettait de garder le buff ET
        // libérer le slot pour un autre passif en même temps (trouvé par Florian, 2026-10-05,
        // après le verrou CD/combat ajouté plus tôt — qui empêche le SWAP mais pas ce souci
        // précis, qui concerne le buff lui-même, pas le slot). Même pattern que
        // Player.UnequipTalisman. `outgoing != passive` : pas de révocation si on réassigne le
        // même passif au même slot (pas un vrai retrait).
        var outgoing = _player.equippedPassives[index];
        if (outgoing != null && outgoing != passive && outgoing.effects != null)
        {
            foreach (var effect in outgoing.effects)
                if (effect != null && effect.effectType == PassiveEffectType.Buff && effect.buffToApply != null)
                    _player.statusEffects?.RemoveBuff(effect.buffToApply);
        }

        // Retire le passif de tout autre slot où il apparaîtrait déjà — évite un double-proc
        // (PassiveSkillSystem itère equippedPassives sans déduplication).
        if (passive != null)
        {
            for (int i = 0; i < _player.equippedPassives.Length; i++)
            {
                if (i == index) continue;
                if (_player.equippedPassives[i] == passive)
                {
                    _player.equippedPassives[i] = null;
                    _slotUIs[i]?.SetPassif(null);
                }
            }
        }

        _player.equippedPassives[index] = passive;
        _slotUIs[index]?.SetPassif(passive);
        Debug.Log($"[PASSIF] Slot {index} → {passive?.name ?? "vide"}");
    }

    /// <summary>Échange le contenu de 2 slots SANS révoquer aucun buff — les deux passifs restent
    /// équipés (juste relocalisés), contrairement à SetPassifAtSlot qui révoque le buff du passif
    /// SORTANT (un vrai retrait, pas une relocalisation). Utilisé par SkillDragDrop.HandleSlotSwap
    /// pour l'échange slot↔slot — appeler SetPassifAtSlot deux fois de suite pour un swap
    /// révoquerait À TORT les buffs des deux passifs au passage (chacun "sort" momentanément
    /// avant d'être replacé ailleurs), trouvé en corrigeant le souci ci-dessus, 2026-10-05.</summary>
    public void SwapPassifs(int indexA, int indexB)
    {
        if (_player == null) _player = FindObjectOfType<Player>();
        if (_player == null) return;
        if (indexA < 0 || indexA >= 3 || indexB < 0 || indexB >= 3 || indexA == indexB) return;

        var a = _player.equippedPassives[indexA];
        var b = _player.equippedPassives[indexB];

        _player.equippedPassives[indexA] = b;
        _player.equippedPassives[indexB] = a;
        _slotUIs[indexA]?.SetPassif(b);
        _slotUIs[indexB]?.SetPassif(a);
        Debug.Log($"[PASSIF] Échange slots {indexA}↔{indexB}");
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
public class PassifSlotBarUI : MonoBehaviour, IPointerClickHandler
{
    [HideInInspector] public Image           passifIcon;
    [HideInInspector] public Image           cdOverlay;
    [HideInInspector] public TextMeshProUGUI cdText;
    [HideInInspector] public Image             incompatibleOverlay;
    [HideInInspector] public PassiveDragSource dragSource;
    [HideInInspector] public int               slotIndex = -1;

    private PassiveSkillData _currentPassif;
    private Player           _player;

    private static readonly Color EmptyColor = new Color(0f, 0f, 0f, 0.4f);

    /// <summary>Clic droit — vide le slot. Exige la SkillLibrary ouverte : c'est le geste qui
    /// rend le clic droit VOLONTAIRE (on ne vide jamais un slot par un clic droit machinal en
    /// plein combat).</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right) return;
        if (_currentPassif == null || slotIndex < 0) return;
        if (SkillLibraryUI.Instance == null || !SkillLibraryUI.Instance.IsOpen) return;
        PassifBarUI.Instance?.SetPassifAtSlot(slotIndex, null);
    }

    // Pas de coroutine/event — poll chaque frame, même pattern que ConsoSlotBarUI.Update().
    // Jamais implémenté jusqu'ici (slot restait toujours sans overlay de cooldown, même après
    // un déclenchement réel) — demande Florian.
    private void Update()
    {
        if (_currentPassif == null) return;

        if (PassiveSkillSystem.Instance != null)
        {
            float remaining = PassiveSkillSystem.Instance.GetCooldownRemaining(_currentPassif);
            SetCooldown(remaining, _currentPassif.cooldown);
        }

        // Overlay rouge "IsIncompatible" si l'arme en main ne matche plus — demande Florian.
        if (incompatibleOverlay != null)
        {
            if (_player == null) _player = FindObjectOfType<Player>();
            WeaponType family = (_player?.equippedWeapon?.weaponType ?? WeaponType.UnArmed).GetStartingFamily();
            incompatibleOverlay.gameObject.SetActive(!_currentPassif.IsCompatibleWith(family));
        }
    }

    public void Init()
    {
        if (cdOverlay != null)
        {
            cdOverlay.type       = Image.Type.Filled;
            cdOverlay.fillMethod = Image.FillMethod.Radial360;
            cdOverlay.fillAmount = 0f;
            cdOverlay.gameObject.SetActive(false);
        }

        // Désactivé par défaut — Update() ne le touche jamais tant que le slot est vide
        // (_currentPassif == null, return anticipé), donc sans ce défaut explicite il garde
        // l'état actif de départ dans l'Inspector — vécu : slots vides affichant l'overlay rouge.
        if (incompatibleOverlay != null) incompatibleOverlay.gameObject.SetActive(false);
    }

    public void SetPassif(PassiveSkillData passive)
    {
        _currentPassif = passive;
        if (dragSource != null) dragSource.passive = passive;
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

        GetComponent<TooltipTrigger>()?.SetPassiveSkill(passive);
    }

    public void SetCooldown(float remaining, float total)
    {
        bool onCD = remaining > 0f && total > 0f;
        if (cdOverlay != null) { cdOverlay.gameObject.SetActive(onCD); cdOverlay.fillAmount = onCD ? remaining / total : 0f; }
        if (cdText    != null) { cdText.gameObject.SetActive(onCD);    cdText.text = onCD ? Mathf.CeilToInt(remaining).ToString() : ""; }
    }
}
