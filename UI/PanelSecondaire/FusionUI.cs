using UnityEngine;
using UnityEngine.UI;
using TMPro;

// =============================================================
// FUSIONUI.CS — Fusion de gants/bottes (Cordonnier, S0 → S6)
// Path : Assets/Scripts/UI/PanelSecondaire/FusionUI.cs
// Voir docs/superpowers/specs/2026-09-04-fusion-system-design.md
//
// 3 slots : Slot 1 + Slot 2 (entrées, glissées depuis l'inventaire) + Slot 3 (preview
// lecture seule, recalculée live). Bouton Fuse désactivé tant que : un slot est vide,
// slot1 == slot2 (même InventoryItem), ou la combinaison dépasse S6.
//
// Résolution : ProgressBarUI (même barre que Forge/Rareté/Craft — BarType.Craft) puis
// FusionSystem.TryFuseXxx. Slot1 ET Slot2 ne sont retirés de l'inventaire que si un jet a
// réellement eu lieu (Success/Failure) — seul un succès ajoute le résultat. Pour les issues
// sans jet (InvalidTarget/MissingResource/InvalidCombo/SameItem), rien n'est consommé et les
// 2 slots restent en place.
// =============================================================
public class FusionUI : MonoBehaviour
{
    public static FusionUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject panel;
    public Button     closeButton;

    [Header("Slots d'entrée (drag & drop)")]
    public FusionSlotDropTarget slot1DropTarget;
    public FusionSlotDropTarget slot2DropTarget;
    public Image           slot1Icon;
    public Image           slot2Icon;
    public TextMeshProUGUI slot1Label; // "S{n} — {nom}" ou "Glisse un objet ici"
    public TextMeshProUGUI slot2Label;

    [Header("Slot 3 — preview résultat")]
    public Image           resultIcon;
    public TextMeshProUGUI resultLabel;       // "S{target} — {nom}"
    public TextMeshProUGUI resultResistances; // "Feu 21% | Eau 21% | ..."
    public TextMeshProUGUI successRateText;   // "Réussite : 80%"
    public TextMeshProUGUI costText;          // "Coût : 150 Aeris"
    public Button           fuseButton;

    private Player        _player;
    private InventoryItem  _slot1Item;
    private InventoryItem  _slot2Item;
    private bool           _channeling;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (panel != null) panel.SetActive(false);
    }

    private void Start()
    {
        closeButton?.onClick.AddListener(Close);
        fuseButton?.onClick.AddListener(OnFuseClicked);

        if (slot1DropTarget != null) slot1DropTarget.OnItemDropped += OnSlotDropped;
        if (slot2DropTarget != null) slot2DropTarget.OnItemDropped += OnSlotDropped;
    }

    // =========================================================
    // OPEN / CLOSE
    // =========================================================

    public void Open(PNJData pnjData, Player player)
    {
        _player = player;
        // Ne réinitialise pas les slots si une canalisation est en cours (ex: le joueur
        // re-clique l'onglet Fusion pendant le jet) — ResolveFusion() lit encore _slot1Item/
        // _slot2Item plus tard via le callback ProgressBarUI, les nuller ici causerait une NRE.
        if (!_channeling)
        {
            _slot1Item = null;
            _slot2Item = null;
        }
        if (panel != null) panel.SetActive(true);
        RefreshSlots();
        RefreshPreview();
    }

    /// <summary>Annule une canalisation en cours (rien n'est consommé — voir le onCancel de
    /// StartProgress) avant de fermer, même pattern que ForgeUI/RarityUI — évite de laisser le
    /// panel Fusion visible/orphelin quand PNJWindowUI.Close() ferme la fenêtre parente.</summary>
    public void Close()
    {
        if (_channeling) ProgressBarUI.Instance?.Cancel();
        if (panel != null) panel.SetActive(false);
    }

    // =========================================================
    // SLOTS D'ENTRÉE
    // =========================================================

    private void OnSlotDropped(int slotIndex, InventoryItem item)
    {
        // N'accepte que Gants OU Bottes (jamais un mélange, jamais autre chose)
        bool isGloves = item.GlovesInstance != null;
        bool isBoots  = item.BootsInstance  != null;
        if (!isGloves && !isBoots) return;

        // Refuse un item actuellement ÉQUIPÉ (glissé depuis le CharacterPanel via
        // EquipmentSlotHandler) — son instance n'est plus dans InventorySystem._items, donc
        // RemoveItem() échouerait silencieusement à la résolution : le joueur garderait la
        // pièce équipée ET obtiendrait quand même le résultat de fusion (duplication).
        object underlyingInstance = isGloves ? (object)item.GlovesInstance : item.BootsInstance;
        if (InventorySystem.Instance?.GetItemByInstance(underlyingInstance) == null)
        {
            Debug.LogWarning("[FUSION] Déséquipe d'abord cette pièce avant de la fusionner.");
            return;
        }

        // Si l'autre slot est déjà rempli, il doit être du même type (gants+gants ou bottes+bottes)
        var other = slotIndex == 0 ? _slot2Item : _slot1Item;
        if (other != null)
        {
            bool otherIsGloves = other.GlovesInstance != null;
            if (otherIsGloves != isGloves) return; // type incompatible, drop ignoré
        }

        if (slotIndex == 0) _slot1Item = item;
        else                 _slot2Item = item;

        RefreshSlots();
        RefreshPreview();
    }

    private void RefreshSlots()
    {
        if (slot1Icon  != null) slot1Icon.sprite  = _slot1Item?.Icon;
        if (slot1Icon  != null) slot1Icon.enabled  = _slot1Item?.Icon != null;
        if (slot1Label != null) slot1Label.text    = _slot1Item != null
            ? $"S{GetFusionLevel(_slot1Item)} — {_slot1Item.Name}"
            : "Glisse un objet ici";

        if (slot2Icon  != null) slot2Icon.sprite  = _slot2Item?.Icon;
        if (slot2Icon  != null) slot2Icon.enabled  = _slot2Item?.Icon != null;
        if (slot2Label != null) slot2Label.text    = _slot2Item != null
            ? $"S{GetFusionLevel(_slot2Item)} — {_slot2Item.Name}"
            : "Glisse un objet ici";
    }

    private int GetFusionLevel(InventoryItem item)
        => item.GlovesInstance?.fusionLevel ?? item.BootsInstance?.fusionLevel ?? 0;

    // =========================================================
    // PREVIEW (SLOT 3) + VALIDATION
    // =========================================================

    private void RefreshPreview()
    {
        bool bothFilled = _slot1Item != null && _slot2Item != null;
        bool sameItem   = bothFilled && (
            (_slot1Item.GlovesInstance != null && ReferenceEquals(_slot1Item.GlovesInstance, _slot2Item.GlovesInstance)) ||
            (_slot1Item.BootsInstance  != null && ReferenceEquals(_slot1Item.BootsInstance,  _slot2Item.BootsInstance))
        );

        if (!bothFilled || sameItem)
        {
            if (resultIcon  != null) resultIcon.enabled = false;
            if (resultLabel != null) resultLabel.text    = sameItem ? "Objet identique aux 2 slots" : "";
            if (resultResistances != null) resultResistances.text = "";
            if (successRateText   != null) successRateText.text   = "";
            if (costText           != null) costText.text           = "";
            if (fuseButton          != null) fuseButton.interactable = false;
            return;
        }

        int level1 = GetFusionLevel(_slot1Item);
        int level2 = GetFusionLevel(_slot2Item);
        bool canFuse = FusionSystem.Instance != null && FusionSystem.Instance.CanFuse(level1, level2, out int target);

        if (!canFuse)
        {
            if (resultIcon  != null) resultIcon.enabled = false;
            if (resultLabel != null) resultLabel.text    = $"Combo invalide — dépasse S6 (S{level1}+S{level2} → S{level1 + level2 + 1})";
            if (resultResistances != null) resultResistances.text = "";
            if (successRateText   != null) successRateText.text   = "";
            if (costText           != null) costText.text           = "";
            if (fuseButton          != null) fuseButton.interactable = false;
            return;
        }

        var tier = FusionSystem.Instance.table.GetTier(target);

        if (resultIcon  != null) { resultIcon.sprite = _slot2Item.Icon; resultIcon.enabled = _slot2Item.Icon != null; }
        if (resultLabel != null) resultLabel.text     = $"S{target} — {_slot2Item.Name}";
        if (resultResistances != null) resultResistances.text = BuildResistancePreviewText();
        if (successRateText   != null) successRateText.text   = tier != null ? $"Réussite : {tier.successRate:P0}" : "";
        if (costText           != null) costText.text           = tier != null ? $"Coût : {tier.aerisCost} Aeris" : "";
        if (fuseButton          != null) fuseButton.interactable = tier != null;
    }

    private string BuildResistancePreviewText()
    {
        if (_slot1Item.GlovesInstance != null && _slot2Item.GlovesInstance != null)
        {
            var g1 = _slot1Item.GlovesInstance;
            var g2 = _slot2Item.GlovesInstance;
            return $"Feu {(g1.resistFire + g2.resistFire):P0} | Eau {(g1.resistWater + g2.resistWater):P0} | " +
                   $"Foudre {(g1.resistLightning + g2.resistLightning):P0} | Terre {(g1.resistEarth + g2.resistEarth):P0} | " +
                   $"Nature {(g1.resistNature + g2.resistNature):P0} | Ténèbres {(g1.resistDarkness + g2.resistDarkness):P0} | " +
                   $"Lumière {(g1.resistLight + g2.resistLight):P0}";
        }
        if (_slot1Item.BootsInstance != null && _slot2Item.BootsInstance != null)
        {
            var b1 = _slot1Item.BootsInstance;
            var b2 = _slot2Item.BootsInstance;
            return $"Feu {(b1.resistFire + b2.resistFire):P0} | Eau {(b1.resistWater + b2.resistWater):P0} | " +
                   $"Foudre {(b1.resistLightning + b2.resistLightning):P0} | Terre {(b1.resistEarth + b2.resistEarth):P0} | " +
                   $"Nature {(b1.resistNature + b2.resistNature):P0} | Ténèbres {(b1.resistDarkness + b2.resistDarkness):P0} | " +
                   $"Lumière {(b1.resistLight + b2.resistLight):P0}";
        }
        return "";
    }

    // =========================================================
    // RÉSOLUTION
    // =========================================================

    private void OnFuseClicked()
    {
        if (_channeling) return;
        if (_slot1Item == null || _slot2Item == null) return;
        bool sameItem = (_slot1Item.GlovesInstance != null && ReferenceEquals(_slot1Item.GlovesInstance, _slot2Item.GlovesInstance)) ||
                        (_slot1Item.BootsInstance  != null && ReferenceEquals(_slot1Item.BootsInstance,  _slot2Item.BootsInstance));
        if (sameItem) return;

        int level1 = GetFusionLevel(_slot1Item);
        int level2 = GetFusionLevel(_slot2Item);
        if (FusionSystem.Instance == null || !FusionSystem.Instance.CanFuse(level1, level2, out _)) return;

        _channeling = true;
        if (fuseButton != null) fuseButton.interactable = false;

        ProgressBarUI.Instance?.StartProgress(
            label:        "Fusion en cours...",
            duration:     FusionSystem.Instance.table.channelDuration,
            onComplete:   ResolveFusion,
            onCancel:     () => { _channeling = false; if (fuseButton != null) fuseButton.interactable = true; },
            type:         ProgressBarUI.BarType.Craft,
            followTarget: _player.transform
        );
    }

    private void ResolveFusion()
    {
        _channeling = false;

        bool isGloves = _slot1Item.GlovesInstance != null;
        InventoryItem resultItem = null;
        FusionResult outcome;

        if (isGloves)
        {
            outcome = FusionSystem.Instance.TryFuseGloves(_slot1Item.GlovesInstance, _slot2Item.GlovesInstance, _player, out var result);
            if (outcome == FusionResult.Success) resultItem = new InventoryItem(result);
        }
        else
        {
            outcome = FusionSystem.Instance.TryFuseBoots(_slot1Item.BootsInstance, _slot2Item.BootsInstance, _player, out var result);
            if (outcome == FusionResult.Success) resultItem = new InventoryItem(result);
        }

        // InvalidTarget/MissingResource/InvalidCombo/SameItem → aucun jet n'a eu lieu et aucun
        // coût n'a été consommé (voir FusionSystem.TryFuseGloves/Boots) : rien ne doit être
        // perdu. On garde les 2 slots tels quels pour que le joueur puisse réessayer sans
        // re-glisser ses objets (ex: FusionSystem.Instance.table non assigné en Inspector).
        if (outcome != FusionResult.Success && outcome != FusionResult.Failure)
        {
            Debug.LogWarning($"[FUSION] Fusion non résolue ({outcome}) — aucune pièce perdue, rien n'a été consommé.");
            RefreshSlots();
            RefreshPreview();
            if (fuseButton != null) fuseButton.interactable = true;
            return;
        }

        // Slot1 et Slot2 sont retirés — succès ou échec (GDD : les deux détruites). N'ajoute le
        // résultat que si les DEUX retraits ont réellement réussi, sinon la pièce fusionnée
        // pourrait être dupliquée avec l'original (ex: instance déjà absente de l'inventaire).
        bool removed1 = InventorySystem.Instance != null && InventorySystem.Instance.RemoveItem(_slot1Item);
        bool removed2 = InventorySystem.Instance != null && InventorySystem.Instance.RemoveItem(_slot2Item);

        if (!removed1 || !removed2)
        {
            Debug.LogError($"[FUSION] Retrait inventaire échoué (slot1={removed1}, slot2={removed2}) — résultat NON ajouté pour éviter une duplication.");
        }
        else if (outcome == FusionResult.Success && resultItem != null)
        {
            if (!InventorySystem.Instance.AddItem(resultItem))
                Debug.LogError("[FUSION] Réussite mais ajout du résultat à l'inventaire impossible (inventaire plein ?) — pièce perdue.");
        }

        Debug.Log(outcome == FusionResult.Success
            ? "[FUSION] Réussite — nouvelle pièce ajoutée à l'inventaire."
            : $"[FUSION] {outcome} — les 2 pièces sont perdues.");

        _slot1Item = null;
        _slot2Item = null;
        RefreshSlots();
        RefreshPreview();
        if (fuseButton != null) fuseButton.interactable = false;
    }
}
