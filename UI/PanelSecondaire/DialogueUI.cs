using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// =============================================================
// DIALOGUEUI.CS — Interface de dialogue PNJ (tous types)
// Path : Assets/Scripts/UI/PanelCenter/DialogueUI.cs
// AetherTree GDD v31 — §19 / §25
//
// Un seul panel pour tous les PNJ — Merchant, Blacksmith, Quest...
// Dialogue 100% statique (texte + options fixes définis sur le DialogueData) —
// l'ancien système de stage dynamique (isDynamicQuestStage, une quête à la fois
// générée à runtime dans la bulle) est retiré le 2026-10-04 (Florian) au profit
// de PNJQuestBoardUI (panel séparé, grille de toutes les quêtes du PNJ) —
// ouvert via une option DialogueAction.OpenQuestLog. Les options statiques
// AcceptQuest/TurnInQuest (DialogueOption.questData) restent utilisables pour
// une quête ponctuelle directement intégrée à un stage de dialogue.
//
// QuestDialogueUI → SUPPRIMÉ.
//
// Hiérarchie Unity :
//   DialogueUI (script ici, désactivé par défaut)
//     Panel_Dialogue
//       Panel_Portrait
//         Img_Background  ← imgBackground
//         Img_Portrait    ← imgPortrait
//       Panel_Content
//         Txt_PNJName     ← txtPNJName
//         Txt_Dialogue    ← txtDialogue
//       Panel_Options (HLG)
//         [boutons instanciés dynamiquement]
// =============================================================

public class DialogueUI : MonoBehaviour
{
    public static DialogueUI Instance { get; private set; }

    [Header("Panel principal")]
    public GameObject panelDialogue;

    [Header("Portrait")]
    public Image           imgBackground;
    public Image           imgPortrait;

    [Header("Texte")]
    public TextMeshProUGUI txtPNJName;
    public TextMeshProUGUI txtDialogue;

    [Header("Options")]
    public Transform  panelOptions;
    public GameObject optionButtonPrefab;

    [Header("Couleurs fond portrait par type")]
    public Color colorMerchant   = new Color(0.20f, 0.60f, 0.20f);
    public Color colorGuard      = new Color(0.50f, 0.50f, 0.60f);
    public Color colorQuest      = new Color(0.20f, 0.40f, 0.60f);
    public Color colorDecorative = new Color(0.35f, 0.35f, 0.35f);
    public Color colorDefault    = new Color(0.25f, 0.25f, 0.25f);

    private PNJ              _currentPNJ;
    private Player           _currentPlayer;
    private List<GameObject> _optionButtons = new List<GameObject>();

    // =========================================================
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (panelDialogue != null) panelDialogue.SetActive(false);
    }

    // =========================================================
    // API — appelée par PNJ.cs
    // =========================================================

    public void OpenDialogue(PNJ pnj, DialogueStage stage, Player player)
    {
        if (pnj == null || stage == null || player == null) return;
        _currentPNJ    = pnj;
        _currentPlayer = player;

        var pc = player.GetComponent<PlayerController>();
        if (pc != null) pc.enabled = false;

        if (panelDialogue != null) panelDialogue.SetActive(true);

        ShowStage(stage);
    }

    public void ShowStage(DialogueStage stage)
    {
        if (stage == null) { CloseDialogue(); return; }

        if (_currentPNJ?.data != null)
        {
            SetText(txtPNJName, _currentPNJ.data.pnjName);
            SetPortrait(_currentPNJ.data.portrait, _currentPNJ.data.pnjType);
        }

        SetText(txtDialogue, stage.text);
        BuildOptions(stage);
    }

    public void CloseDialogue()
    {
        if (_currentPlayer != null)
        {
            var pc = _currentPlayer.GetComponent<PlayerController>();
            if (pc != null) pc.enabled = true;
        }
        if (panelDialogue != null) panelDialogue.SetActive(false);
        ClearOptions();
        _currentPNJ    = null;
        _currentPlayer = null;
    }

    public bool IsOpen => panelDialogue != null && panelDialogue.activeSelf;

    // =========================================================
    // OPTIONS
    // =========================================================

    private void BuildOptions(DialogueStage stage)
    {
        ClearOptions();
        if (panelOptions == null || optionButtonPrefab == null) return;

        // Stage normal
        if (stage.options == null || stage.options.Count == 0)
        {
            SpawnButton("Fermer", OnClickClose);
            return;
        }

        foreach (var opt in stage.options)
        {
            var captured = opt;
            string label = opt.action == DialogueAction.PurifyAura
                ? BuildPurifyAuraLabel(opt.label, _currentPlayer)
                : opt.label;
            SpawnButton(label, () => OnOptionClicked(captured));
        }
    }

    /// <summary>Ajoute le coût Aeris courant au label du bouton Purifier — le coût dépend du
    /// auraRank ACTUEL du joueur (voir Player.prestigeAuraData.auraTiers), jamais fixé sur
    /// l'asset DialogueOption (partagé entre tous les joueurs/paliers), donc calculé ici à
    /// chaque affichage plutôt que stocké en dur dans le label texte de l'option.</summary>
    private string BuildPurifyAuraLabel(string baseLabel, Player player)
    {
        var tiers = player?.prestigeAuraData?.auraTiers;
        int rank = player?.auraRank ?? 0;
        if (tiers == null || rank <= 0 || rank >= tiers.Count) return baseLabel;
        return $"{baseLabel} ({tiers[rank].purificationAerisCost} Aeris)";
    }

    // =========================================================
    // CLIC OPTION NORMALE
    // =========================================================

    private void OnOptionClicked(DialogueOption option)
    {
        if (option == null) { OnClickClose(); return; }

        // OpenShop/OpenForge/OpenRarity/OpenRuneUI ne sont PAS dispatchées ici — elles
        // le sont déjà par PNJ.HandleDialogueAction, appelé juste en dessous via
        // SelectOption(). Un double dispatch ici causait une double ouverture de
        // panel (ex : InventoryUI.Open() appelé deux fois par clic).
        switch (option.action)
        {
            case DialogueAction.AcceptQuest:
                if (option.questData != null && _currentPlayer != null)
                {
                    QuestSystem.Instance?.AcceptQuest(option.questData, _currentPlayer);
                    _currentPNJ?.SelectOption(option, _currentPlayer);
                    CloseDialogue();
                    return;
                }
                break;
            case DialogueAction.TurnInQuest:
                if (option.questData != null && _currentPlayer != null)
                {
                    QuestSystem.Instance?.TurnInQuest(option.questData, _currentPlayer);
                    _currentPNJ?.SelectOption(option, _currentPlayer);
                    CloseDialogue();
                    return;
                }
                break;
            case DialogueAction.CloseDialogue:
                _currentPNJ?.SelectOption(option, _currentPlayer);
                CloseDialogue();
                return;
        }

        if (option.nextStageID < 0)
        {
            _currentPNJ?.SelectOption(option, _currentPlayer);
            CloseDialogue();
            return;
        }

        _currentPNJ?.SelectOption(option, _currentPlayer);
    }

    private void OnClickClose()
    {
        _currentPNJ?.SelectOption(null, _currentPlayer);
        CloseDialogue();
    }

    // =========================================================
    // HELPERS
    // =========================================================

    private void SpawnButton(string label, System.Action onClick)
    {
        var go = Instantiate(optionButtonPrefab, panelOptions);
        _optionButtons.Add(go);
        var txt = go.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null) txt.text = label;
        var btn = go.GetComponent<Button>();
        if (btn != null) btn.onClick.AddListener(() => onClick());
    }

    private void ClearOptions()
    {
        foreach (var b in _optionButtons) if (b != null) Destroy(b);
        _optionButtons.Clear();
    }

    private void SetPortrait(Sprite spr, PNJType type)
    {
        if (imgPortrait   != null) { imgPortrait.sprite = spr; imgPortrait.enabled = spr != null; }
        if (imgBackground != null)   imgBackground.color = GetPortraitColor(type);
    }

    private Color GetPortraitColor(PNJType type) => type switch
    {
        // Merchant couvre toute spécialité boutique depuis le 2026-10-04 (ex-Forge/Antiquarian/
        // Cordonnier/Cook/Tinkerer/Jeweler/Hatter/CraftStation, voir PNJData.shopSpecialty) —
        // même couleur de portrait pour toutes, pas de teinte par spécialité (jamais demandé).
        PNJType.Merchant   => colorMerchant,
        PNJType.Guard      => colorGuard,
        PNJType.Quest      => colorQuest,
        PNJType.Decorative => colorDecorative,
        _                  => colorDefault
    };

    private void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null) label.text = value ?? "";
    }
}