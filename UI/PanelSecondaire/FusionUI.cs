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
// Slot1/Slot2 sont retirés de InventorySystem DÈS le dépôt (OnSlotDropped), pas seulement à
// la résolution — lisibilité (l'item ne traîne plus visuellement dans la grille ET dans le
// slot) et surtout ça empêche STRUCTURELLEMENT de reposer le même item dans l'autre slot
// (il n'est plus dans InventorySystem._items, GetItemByInstance échoue). Remis en
// inventaire si : le slot est remplacé par un autre item, le panel se ferme sans résoudre,
// le joueur re-glisse l'item hors du slot (StagedItemDragSource), ou l'issue de résolution
// n'a impliqué aucun jet (InvalidTarget/MissingResource/InvalidCombo/SameItem).
//
// Résolution : ProgressBarUI (même barre que Forge/Rareté/Craft — BarType.Craft) puis
// FusionSystem.TryFuseXxx. Succès ou échec = les 2 pièces restent définitivement hors de
// l'inventaire (déjà retirées au dépôt) — seul un succès ajoute le résultat.
//
// Ne peut contenir QUE des objets de l'inventaire — un équipement porté (CharacterPanel) est
// toujours rejeté par OnSlotDropped (GetItemByInstance), jamais insérable ici.
// =============================================================
public class FusionUI : MonoBehaviour
{
    public static FusionUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject panel;

    [Header("Header")]
    public TextMeshProUGUI titleText;
    public Button          closeButton;

    [Header("Slots d'entrée (drag & drop)")]
    public FusionSlotDropTarget slot1DropTarget;
    public FusionSlotDropTarget slot2DropTarget;
    public Image           slot1Icon;
    public Image           slot2Icon;

    [Header("Slot 3 — preview résultat")]
    public Image           resultIcon;
    public TextMeshProUGUI successRateText;   // "Réussite : 80%"
    public TextMeshProUGUI costText;          // "Coût : 150 Aeris"
    public Button           fuseButton;

    [Header("Résultat de la dernière tentative")]
    [Tooltip("\"Réussite !\" / \"Échec — les 2 pièces sont détruites\" — vidé dès qu'un slot change.")]
    public TextMeshProUGUI resultOutcomeText;

    private const string ColorOk  = "#4CDB57"; // même famille que ForgeUI/CraftPanelUI
    private const string ColorBad = "#E0455F";

    // Nom/palier/résistances/défenses — plus de texte dédié, tout passe par le
    // TooltipTrigger posé sur chaque slot (Slot1/Slot2/Slot3 dans l'Inspector) au survol,
    // même pattern que le reste du jeu (SkillBar, PassifBar, ConsoBar...).

    private Player        _player;
    private InventoryItem  _slot1Item;
    private InventoryItem  _slot2Item;
    private bool           _channeling;

    /// <summary>True pendant la canalisation de fusion — FusionSlotDropTarget ignore les
    /// drops tant que c'est actif (pas de changement d'objet en cours de résolution).</summary>
    public bool IsChanneling => _channeling;

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

        // Drag inversé — re-glisser l'item hors du slot pour le remettre en inventaire.
        slot1Icon?.GetComponent<StagedItemDragSource>()?.Init(() => _slot1Item, OnSlot1ReturnedToInventory);
        slot2Icon?.GetComponent<StagedItemDragSource>()?.Init(() => _slot2Item, OnSlot2ReturnedToInventory);
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
            RestoreAndClearSlots();
            if (resultOutcomeText != null) resultOutcomeText.text = "";
        }
        if (panel != null) panel.SetActive(true);
        if (titleText != null) titleText.text = "Fusion";
        RefreshSlots();
        RefreshPreview();
    }

    /// <summary>Annule une canalisation en cours (rien n'est consommé — voir le onCancel de
    /// StartProgress) avant de fermer, même pattern que ForgeUI/RarityUI — évite de laisser le
    /// panel Fusion visible/orphelin quand PNJWindowUI.Close() ferme la fenêtre parente.
    /// Remet aussi les 2 slots en inventaire — fermer sans avoir lancé la fusion ne doit
    /// jamais faire "disparaître" les objets déposés.</summary>
    public void Close()
    {
        if (_channeling) ProgressBarUI.Instance?.Cancel();
        RestoreAndClearSlots();
        if (panel != null) panel.SetActive(false);
    }

    /// <summary>Remet en inventaire tout ce qui est dans les 2 slots, puis les vide — jamais
    /// pendant une résolution en cours (ResolveFusion gère lui-même le sort final).</summary>
    private void RestoreAndClearSlots()
    {
        if (_slot1Item != null) { InventorySystem.Instance?.AddItem(_slot1Item); _slot1Item = null; }
        if (_slot2Item != null) { InventorySystem.Instance?.AddItem(_slot2Item); _slot2Item = null; }
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

        // Remet l'ancien occupant de CE slot en inventaire avant de le remplacer — le joueur
        // peut glisser un nouvel objet directement par-dessus sans d'abord vider le slot.
        var previous = slotIndex == 0 ? _slot1Item : _slot2Item;
        if (previous != null) InventorySystem.Instance?.AddItem(previous);

        if (slotIndex == 0) _slot1Item = item;
        else                 _slot2Item = item;

        // Retiré dès le dépôt — lisibilité + empêche structurellement de reposer le même
        // item dans l'autre slot (le GetItemByInstance ci-dessus le rejettera, plus dans
        // _items tant qu'il reste ici).
        InventorySystem.Instance?.RemoveItem(item);

        if (resultOutcomeText != null) resultOutcomeText.text = "";
        RefreshSlots();
        RefreshPreview();
    }

    private void OnSlot1ReturnedToInventory()
    {
        if (_slot1Item == null) return;
        InventorySystem.Instance?.AddItem(_slot1Item);
        _slot1Item = null;
        if (resultOutcomeText != null) resultOutcomeText.text = "";
        RefreshSlots();
        RefreshPreview();
    }

    private void OnSlot2ReturnedToInventory()
    {
        if (_slot2Item == null) return;
        InventorySystem.Instance?.AddItem(_slot2Item);
        _slot2Item = null;
        if (resultOutcomeText != null) resultOutcomeText.text = "";
        RefreshSlots();
        RefreshPreview();
    }

    private static readonly Color FilledSlotColor = Color.white;
    private static readonly Color EmptySlotColor  = new Color(1f, 1f, 1f, 0.15f);

    // ⚠ Ne JAMAIS mettre Icon.enabled = false sur un slot vide — FusionSlotDropTarget est
    // sur ce même GameObject, et Unity ne raycast pas un Graphic désactivé : un slot "vide
    // et invisible" devient aussi indroppable. On garde l'Image active en permanence, on
    // estompe juste sa couleur (même pattern que InventoryItemCell.bgImage).
    private void RefreshSlots()
    {
        if (slot1Icon != null)
        {
            slot1Icon.sprite = _slot1Item?.Icon;
            slot1Icon.color  = _slot1Item?.Icon != null ? FilledSlotColor : EmptySlotColor;
            slot1Icon.GetComponent<TooltipTrigger>()?.SetItem(_slot1Item);
        }

        if (slot2Icon != null)
        {
            slot2Icon.sprite = _slot2Item?.Icon;
            slot2Icon.color  = _slot2Item?.Icon != null ? FilledSlotColor : EmptySlotColor;
            slot2Icon.GetComponent<TooltipTrigger>()?.SetItem(_slot2Item);
        }
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
            if (resultIcon != null)
            {
                resultIcon.sprite = null;
                resultIcon.color  = EmptySlotColor;
                resultIcon.GetComponent<TooltipTrigger>()?.SetItem(null);
            }
            if (successRateText != null) successRateText.text = "";
            if (costText         != null) costText.text         = "";
            if (fuseButton        != null) fuseButton.interactable = false;
            return;
        }

        int level1 = GetFusionLevel(_slot1Item);
        int level2 = GetFusionLevel(_slot2Item);
        int target = 0;
        bool canFuse = FusionSystem.Instance != null && FusionSystem.Instance.CanFuse(level1, level2, out target);

        if (!canFuse)
        {
            if (resultIcon != null)
            {
                resultIcon.sprite = null;
                resultIcon.color  = EmptySlotColor;
                resultIcon.GetComponent<TooltipTrigger>()?.SetItem(null);
            }
            if (successRateText != null) successRateText.text = $"Combo invalide — dépasse S6 (S{level1}+S{level2} → S{level1 + level2 + 1})";
            if (costText         != null) costText.text         = "";
            if (fuseButton        != null) fuseButton.interactable = false;
            return;
        }

        var tier = FusionSystem.Instance.table.GetTier(target);

        // Preview réelle — Fuse() est pure (ne mute jamais slot1/slot2, aucune consommation),
        // donc l'appeler ici juste pour afficher le tooltip du résultat exact (nom, def,
        // résistances) est sûr. L'objet preview est jeté après ce refresh, jamais persisté —
        // la vraie fusion (ResolveFusion) rappelle Fuse() séparément à la résolution.
        InventoryItem previewItem = BuildPreviewResultItem();
        if (resultIcon != null)
        {
            resultIcon.sprite = previewItem?.Icon;
            resultIcon.color  = previewItem?.Icon != null ? FilledSlotColor : EmptySlotColor;
            resultIcon.GetComponent<TooltipTrigger>()?.SetItem(previewItem);
        }
        if (successRateText != null) successRateText.text = tier != null ? $"Réussite : {Mathf.RoundToInt(tier.successRate * 100f)}%" : "";
        if (costText         != null) costText.text         = tier != null ? $"Coût : {tier.aerisCost} Aeris" : "";
        if (fuseButton        != null) fuseButton.interactable = tier != null;
    }

    private InventoryItem BuildPreviewResultItem()
    {
        if (_slot1Item.GlovesInstance != null && _slot2Item.GlovesInstance != null)
            return new InventoryItem(GlovesInstance.Fuse(_slot1Item.GlovesInstance, _slot2Item.GlovesInstance));
        if (_slot1Item.BootsInstance != null && _slot2Item.BootsInstance != null)
            return new InventoryItem(BootsInstance.Fuse(_slot1Item.BootsInstance, _slot2Item.BootsInstance));
        return null;
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
            if (resultOutcomeText != null)
                resultOutcomeText.text = $"<color={ColorBad}>Fusion impossible ({outcome})</color>";
            RefreshSlots();
            RefreshPreview();
            if (fuseButton != null) fuseButton.interactable = true;
            return;
        }

        // Slot1 et Slot2 sont déjà hors de l'inventaire depuis leur dépôt (OnSlotDropped) —
        // succès ou échec, ils y restent définitivement (GDD : les deux détruites). Un
        // succès ajoute seulement le résultat.
        if (outcome == FusionResult.Success && resultItem != null)
        {
            if (!InventorySystem.Instance.AddItem(resultItem))
                Debug.LogError("[FUSION] Réussite mais ajout du résultat à l'inventaire impossible (inventaire plein ?) — pièce perdue.");
        }

        Debug.Log(outcome == FusionResult.Success
            ? "[FUSION] Réussite — nouvelle pièce ajoutée à l'inventaire."
            : $"[FUSION] {outcome} — les 2 pièces sont perdues.");

        if (resultOutcomeText != null)
            resultOutcomeText.text = outcome == FusionResult.Success
                ? $"<color={ColorOk}>Réussite !</color>"
                : $"<color={ColorBad}>Échec — les 2 pièces sont détruites</color>";

        _slot1Item = null;
        _slot2Item = null;
        RefreshSlots();
        RefreshPreview();
        if (fuseButton != null) fuseButton.interactable = false;
    }
}
