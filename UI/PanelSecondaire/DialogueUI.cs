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
// Les quêtes s'intègrent via stages dynamiques (isDynamicQuestStage)
// et via DialogueAction.AcceptQuest / TurnInQuest sur les options.
//
// Pour un PNJ Quest :
//   Stage 0  : texte normal "Bonjour..."
//   Stage 10 : isDynamicQuestStage = true → texte calculé à runtime
//              boutons Accepter / Récupérer / Partir générés auto
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
    public Color colorBlacksmith = new Color(0.60f, 0.30f, 0.10f);
    public Color colorGuard      = new Color(0.50f, 0.50f, 0.60f);
    public Color colorMayor      = new Color(0.60f, 0.50f, 0.10f);
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

        // Stage dynamique quête : texte calculé à runtime
        string text = stage.isDynamicQuestStage && _currentPNJ?.data != null && _currentPlayer != null
            ? BuildQuestText(_currentPNJ.data, _currentPlayer)
            : stage.text;

        SetText(txtDialogue, text);
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
    // TEXTE DYNAMIQUE QUÊTE
    // Équivalent de get_dialogue_for_quests() (Python v0.1)
    // =========================================================

    private string BuildQuestText(PNJData data, Player player)
    {
        if (QuestSystem.Instance == null || data.availableQuests == null)
            return "Je n'ai rien pour toi pour le moment.";

        // 1. Quête complétée à valider ?
        foreach (var q in data.availableQuests)
        {
            if (q == null) continue;
            if (QuestSystem.Instance.GetQuestState(q) == QuestState.Completed)
                return $"Ho, tu as terminé '{q.questName}' ! Voici ta récompense.";
        }

        // 2. Quête en cours ?
        foreach (var q in data.availableQuests)
        {
            if (q == null) continue;
            if (QuestSystem.Instance.GetQuestState(q) == QuestState.Active)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Alors {player.entityName}, tu avances sur '{q.questName}' ?");
                foreach (int idx in q.GetActiveObjectiveIndices())
                    sb.AppendLine($"• {q.objectives[idx].description}  {q.objectives[idx].ProgressLabel}");
                return sb.ToString().TrimEnd();
            }
        }

        // 3. Nouvelle quête disponible ?
        foreach (var q in data.availableQuests)
        {
            if (q == null) continue;
            if (QuestSystem.Instance.CanAccept(q, player))
                return $"J'ai une mission pour toi : {q.description}";
        }

        // 4. Rien
        return $"Merci {player.entityName}, je n'ai plus rien pour toi pour le moment.";
    }

    // =========================================================
    // OPTIONS
    // =========================================================

    private void BuildOptions(DialogueStage stage)
    {
        ClearOptions();
        if (panelOptions == null || optionButtonPrefab == null) return;

        // Stage quête dynamique → boutons générés selon l'état
        if (stage.isDynamicQuestStage && _currentPNJ?.data != null && _currentPlayer != null)
        {
            BuildQuestButtons(_currentPNJ.data, _currentPlayer);
            return;
        }

        // Stage normal
        if (stage.options == null || stage.options.Count == 0)
        {
            SpawnButton("Fermer", OnClickClose);
            return;
        }

        foreach (var opt in stage.options)
        {
            var captured = opt;
            SpawnButton(opt.label, () => OnOptionClicked(captured));
        }
    }

    private void BuildQuestButtons(PNJData data, Player player)
    {
        if (QuestSystem.Instance == null) return;
        ClearOptions();

        // Priorité 1 : quête complétée → Récupérer seulement
        foreach (var q in data.availableQuests)
        {
            if (q == null) continue;
            if (QuestSystem.Instance.GetQuestState(q) == QuestState.Completed)
            {
                var captured = q;
                SpawnButton("Récupérer", () =>
                {
                    QuestSystem.Instance.TurnInQuest(captured, player);
                    SetText(txtDialogue, BuildQuestText(data, player));
                    BuildQuestButtons(data, player);
                });
                SpawnButton("Partir", OnClickClose);
                return;
            }
        }

        // Priorité 2 : quête en cours → pas de Accepter
        foreach (var q in data.availableQuests)
        {
            if (q == null) continue;
            if (QuestSystem.Instance.GetQuestState(q) == QuestState.Active)
            {
                SpawnButton("Partir", OnClickClose);
                return;
            }
        }

        // Priorité 3 : première quête disponible → Accepter
        foreach (var q in data.availableQuests)
        {
            if (q == null) continue;
            if (QuestSystem.Instance.CanAccept(q, player))
            {
                var captured = q;
                SpawnButton("Accepter", () =>
                {
                    QuestSystem.Instance.AcceptQuest(captured, player);
                    CloseDialogue();
                });
                SpawnButton("Partir", OnClickClose);
                return;
            }
        }

        SpawnButton("Partir", OnClickClose);
    }

    private void RefreshQuestStage()
    {
        if (_currentPNJ?.data == null || _currentPlayer == null) return;
        SetText(txtDialogue, BuildQuestText(_currentPNJ.data, _currentPlayer));
        BuildQuestButtons(_currentPNJ.data, _currentPlayer);
    }

    // =========================================================
    // CLIC OPTION NORMALE
    // =========================================================

    private void OnOptionClicked(DialogueOption option)
    {
        if (option == null) { OnClickClose(); return; }

        switch (option.action)
        {
            case DialogueAction.OpenShop:
                ShopUI.Instance?.OpenShop(_currentPNJ?.data, _currentPlayer);
                break;
            case DialogueAction.OpenForge:
                ForgeUI.Instance?.Open();
                break;
            case DialogueAction.OpenRuneUI:
                Debug.Log("[DialogueUI] RuneUI — TODO Phase 6");
                break;
            case DialogueAction.AcceptQuest:
                if (option.questData != null && _currentPlayer != null)
                {
                    QuestSystem.Instance?.AcceptQuest(option.questData, _currentPlayer);
                    RefreshQuestStage();
                    return;
                }
                break;
            case DialogueAction.TurnInQuest:
                if (option.questData != null && _currentPlayer != null)
                {
                    QuestSystem.Instance?.TurnInQuest(option.questData, _currentPlayer);
                    RefreshQuestStage();
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
        PNJType.Merchant   => colorMerchant,
        PNJType.Blacksmith => colorBlacksmith,
        PNJType.Guard      => colorGuard,
        PNJType.Mayor      => colorMayor,
        PNJType.Quest      => colorQuest,
        PNJType.Decorative => colorDecorative,
        _                  => colorDefault
    };

    private void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null) label.text = value ?? "";
    }
}