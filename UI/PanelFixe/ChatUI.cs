using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// =============================================================
// CHATUI — Fenêtre de chat unifiée (HUD permanent)
// Path : Assets/Scripts/UI/PanelFixe/ChatUI.cs
// AetherTree GDD v31 — voir docs/superpowers/specs/2026-10-05-chat-system-design.md
//
// UN SEUL flux (toutes les lignes mélangées, préfixées/colorées par canal) — PAS des onglets qui
// cachent les autres canaux (design initial faux, corrigé après inspiration Nostale réelle :
// Nostale a une fenêtre unique avec un filtre de catégorie, pas des onglets séparés).
//
// Écho d'Aether IGNORE TOUJOURS le filtre (pas de toggle dédié) — c'est sa raison d'être, voir
// spec section "Nostale : le haut-parleur ignore le filtre de vue temporaire".
//
// SETUP HIERARCHY attendu (Florian construit, ce script fait les Find()/assignations en Inspector
// comme pour les autres UI de cette session) :
//
// ChatPanel (racine, toujours active — HUD permanent)
//   ├── MessageScrollView
//   │     └── Content              (Transform — parent des lignes instanciées, assigner à
//   │                                messageContent)
//   ├── FilterBar
//   │     ├── FilterWorld           (Toggle)
//   │     ├── FilterGuild           (Toggle)
//   │     ├── FilterPrivate         (Toggle)
//   │     ├── FilterNearby          (Toggle)
//   │     └── FilterSystem          (Toggle)
//   │     (pas de FilterAetherEcho — ignore toujours le filtre)
//   ├── InputBar
//   │     ├── ChannelPicker          (TMP_Dropdown)
//   │     ├── InputField             (TMP_InputField)
//   │     └── SendButton             (Button)
//   └── ChatLinePrefab (prefab séparé, pas un enfant actif — glissé en Inspector)
//         └── (TextMeshProUGUI à la racine du prefab)
// =============================================================

public class ChatUI : MonoBehaviour
{
    public static ChatUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject panel;

    [Header("Historique")]
    public Transform  messageContent;
    public GameObject chatLinePrefab;

    [Header("Filtres (un Toggle par canal, PAS AetherEcho — toujours affiché)")]
    public Toggle filterWorld;
    public Toggle filterGuild;
    public Toggle filterPrivate;
    public Toggle filterNearby;
    public Toggle filterSystem;

    [Header("Couleurs par canal")]
    public Color colorWorld      = Color.white;
    public Color colorGuild      = new Color(0.4f, 0.85f, 0.4f);
    public Color colorPrivate    = new Color(0.9f, 0.5f,  0.8f);
    public Color colorNearby     = new Color(0.8f, 0.8f,  0.8f);
    public Color colorSystem     = new Color(0.6f, 0.6f,  0.65f);
    public Color colorAetherEcho = new Color(1f,   0.75f, 0.2f);

    [Header("Saisie")]
    public TMP_Dropdown    channelPicker;
    public TMP_InputField  inputField;
    public Button          sendButton;

    private readonly List<GameObject> _lineObjects = new List<GameObject>();
    private bool _dirty = true;

    private Player _player;

    // Canaux TOUJOURS listés dans le picker, dans cet ordre — AetherEcho est ajouté/retiré
    // dynamiquement en plus de ceux-ci (voir RefreshChannelPicker).
    private static readonly ChatChannel[] BaseSendableChannels =
        { ChatChannel.World, ChatChannel.Guild, ChatChannel.Private, ChatChannel.Nearby };

    private List<ChatChannel> _pickerChannels = new List<ChatChannel>();
    private bool _lastHasAetherEcho = false;

    // =========================================================
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        Subscribe();

        filterWorld  ?.onValueChanged.AddListener(_ => _dirty = true);
        filterGuild  ?.onValueChanged.AddListener(_ => _dirty = true);
        filterPrivate?.onValueChanged.AddListener(_ => _dirty = true);
        filterNearby ?.onValueChanged.AddListener(_ => _dirty = true);
        filterSystem ?.onValueChanged.AddListener(_ => _dirty = true);

        sendButton   ?.onClick.AddListener(OnSendClicked);

        _lastHasAetherEcho = false; // force le premier RefreshChannelPicker() du prochain Update()
        RefreshChannelPicker();
    }

    private void OnDestroy() => Unsubscribe();

    // Même bug que QuestTrackerUI/QuestJournalUI/PNJQuestBoardUI cette session —
    // GameEventBus.Reset() désabonne tout à chaque changement de scène, sans Resubscribe()
    // enregistré ce panel arrêterait de recevoir les nouveaux messages au premier changement de
    // map (ne pas répéter ce bug une 5e fois).
    public void Resubscribe() { Unsubscribe(); Subscribe(); }

    private void Subscribe()   => GameEventBus.OnChatMessage += OnChatMessage;
    private void Unsubscribe() => GameEventBus.OnChatMessage -= OnChatMessage;

    private void OnChatMessage(ChatMessageEvent e) => _dirty = true;

    private void Update()
    {
        if (_dirty)
        {
            _dirty = false;
            Refresh();
        }

        bool hasEcho = ChatSystem.Instance != null && ChatSystem.Instance.HasAetherEcho(_player);
        if (hasEcho != _lastHasAetherEcho)
        {
            _lastHasAetherEcho = hasEcho;
            RefreshChannelPicker();
        }
    }

    // =========================================================
    // AFFICHAGE
    // =========================================================

    private bool IsChannelVisible(ChatChannel channel) => channel switch
    {
        ChatChannel.AetherEcho => true, // ignore toujours le filtre
        ChatChannel.World      => filterWorld   == null || filterWorld.isOn,
        ChatChannel.Guild      => filterGuild   == null || filterGuild.isOn,
        ChatChannel.Private    => filterPrivate == null || filterPrivate.isOn,
        ChatChannel.Nearby     => filterNearby  == null || filterNearby.isOn,
        ChatChannel.System     => filterSystem  == null || filterSystem.isOn,
        _                      => true,
    };

    private Color ChannelColor(ChatChannel channel) => channel switch
    {
        ChatChannel.World      => colorWorld,
        ChatChannel.Guild      => colorGuild,
        ChatChannel.Private    => colorPrivate,
        ChatChannel.Nearby     => colorNearby,
        ChatChannel.System     => colorSystem,
        ChatChannel.AetherEcho => colorAetherEcho,
        _                      => Color.white,
    };

    private string ChannelPrefix(ChatChannel channel) => channel switch
    {
        ChatChannel.World      => "[Monde]",
        ChatChannel.Guild      => "[Famille]",
        ChatChannel.Private    => "[Chuchotement]",
        ChatChannel.Nearby     => "[Alentour]",
        ChatChannel.System     => "[Système]",
        ChatChannel.AetherEcho => "[Écho d'Aether]",
        _                      => "",
    };

    private void Refresh()
    {
        foreach (var go in _lineObjects)
            if (go != null) Destroy(go);
        _lineObjects.Clear();

        if (ChatSystem.Instance == null || messageContent == null || chatLinePrefab == null) return;

        foreach (var line in ChatSystem.Instance.GetHistory())
        {
            if (!IsChannelVisible(line.channel)) continue;

            var go = Instantiate(chatLinePrefab, messageContent);
            _lineObjects.Add(go);

            var txt = go.GetComponentInChildren<TextMeshProUGUI>();
            if (txt == null) continue;

            string sender = string.IsNullOrEmpty(line.sender) ? "" : $"{line.sender} : ";
            txt.text  = $"{ChannelPrefix(line.channel)} {sender}{line.text}";
            txt.color = ChannelColor(line.channel);
        }
    }

    // =========================================================
    // SAISIE / ENVOI
    // =========================================================

    /// <summary>Reconstruit la liste des canaux proposés — appelée dès qu'Update() détecte un
    /// changement de HasAetherEcho (apparition/disparition), PAS une seule fois à Start(), pour
    /// qu'Écho d'Aether disparaisse immédiatement s'il vient d'être consommé.</summary>
    private void RefreshChannelPicker()
    {
        if (channelPicker == null) return;
        if (_player == null) _player = FindObjectOfType<Player>();

        _pickerChannels = new List<ChatChannel>(BaseSendableChannels);
        if (ChatSystem.Instance != null && ChatSystem.Instance.HasAetherEcho(_player))
            _pickerChannels.Add(ChatChannel.AetherEcho);

        var labels = new List<string>();
        foreach (var c in _pickerChannels) labels.Add(ChannelPrefix(c));

        int previousIndex = Mathf.Clamp(channelPicker.value, 0, labels.Count - 1);
        channelPicker.ClearOptions();
        channelPicker.AddOptions(labels);
        channelPicker.value = previousIndex;
    }

    private void OnSendClicked()
    {
        if (inputField == null || string.IsNullOrWhiteSpace(inputField.text)) return;
        if (_player == null) _player = FindObjectOfType<Player>();
        if (_player == null) return;

        ChatChannel channel = ChatChannel.World;
        if (channelPicker != null && _pickerChannels.Count > 0)
        {
            int index = Mathf.Clamp(channelPicker.value, 0, _pickerChannels.Count - 1);
            channel = _pickerChannels[index];
        }

        bool sent = ChatSystem.Instance != null && ChatSystem.Instance.TrySendPlayerMessage(channel, inputField.text, _player);
        if (sent) inputField.text = "";

        // Le dernier Écho d'Aether vient peut-être d'être consommé — re-synchronise le picker
        // tout de suite plutôt que d'attendre la prochaine ouverture.
        RefreshChannelPicker();
    }
}
