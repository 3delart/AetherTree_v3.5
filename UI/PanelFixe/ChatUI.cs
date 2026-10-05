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
//   │     ├── FilterWorld           (Button — pas Toggle, état géré en script avec retint couleur)
//   │     ├── FilterGuild           (Button)
//   │     ├── FilterPrivate         (Button)
//   │     ├── FilterNearby          (Button)
//   │     └── FilterSystem          (Button)
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

    [Header("Filtres (un Button par canal, PAS AetherEcho — toujours affiché)")]
    public Button filterWorld;
    public Button filterGuild;
    public Button filterPrivate;
    public Button filterNearby;
    public Button filterSystem;

    // Un Button n'a pas d'état on/off intégré (contrairement à Toggle) — géré ici à la main, avec
    // retint du bouton selon la couleur du canal (actif = couleur pleine, inactif = gris).
    private bool _showWorld   = true;
    private bool _showGuild   = true;
    private bool _showPrivate = true;
    private bool _showNearby  = true;
    private bool _showSystem  = true;
    private static readonly Color FILTER_OFF_COLOR = new Color(0.3f, 0.3f, 0.35f, 0.5f);

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

        filterWorld  ?.onClick.AddListener(() => ToggleFilter(filterWorld,   colorWorld,   ref _showWorld));
        filterGuild  ?.onClick.AddListener(() => ToggleFilter(filterGuild,   colorGuild,   ref _showGuild));
        filterPrivate?.onClick.AddListener(() => ToggleFilter(filterPrivate, colorPrivate, ref _showPrivate));
        filterNearby ?.onClick.AddListener(() => ToggleFilter(filterNearby,  colorNearby,  ref _showNearby));
        filterSystem ?.onClick.AddListener(() => ToggleFilter(filterSystem,  colorSystem,  ref _showSystem));

        // État visuel initial (tous actifs par défaut) — sans ça les boutons restent dans leur
        // couleur Unity par défaut jusqu'au premier clic, incohérent avec l'état réel (tout visible).
        ApplyFilterVisual(filterWorld,   colorWorld,   _showWorld);
        ApplyFilterVisual(filterGuild,   colorGuild,   _showGuild);
        ApplyFilterVisual(filterPrivate, colorPrivate, _showPrivate);
        ApplyFilterVisual(filterNearby,  colorNearby,  _showNearby);
        ApplyFilterVisual(filterSystem,  colorSystem,  _showSystem);

        sendButton   ?.onClick.AddListener(OnSendClicked);
        inputField   ?.onSubmit.AddListener(_ => OnSendClicked()); // Entrée envoie aussi

        _lastHasAetherEcho = false; // force le premier RefreshChannelPicker() du prochain Update()
        RefreshChannelPicker();
    }

    private void OnDestroy() => Unsubscribe();

    // Même bug que QuestTrackerUI/QuestJournalUI/PNJQuestBoardUI cette session —
    // GameEventBus.Reset() désabonne tout à chaque changement de scène, sans Resubscribe()
    // enregistré ce panel arrêterait de recevoir les nouveaux messages au premier changement de
    // map (ne pas répéter ce bug une 5e fois).
    public void Resubscribe() { Unsubscribe(); Subscribe(); }

    /// <summary>Montre/cache le dock — appelé par UIManager sur GameControls.OpenChat (touche V,
    /// ex-stub SocialUI.SocialTab.Chat jamais implémenté, retiré au profit de ce dock permanent —
    /// Florian, 2026-10-05). Contrairement aux autres panels Social (toggle "ouvert/fermé" complet),
    /// ChatUI reste un HUD permanent par défaut — cette touche ne fait que le masquer/réafficher.</summary>
    public void ToggleVisibility()
    {
        if (panel != null) panel.SetActive(!panel.activeSelf);
    }

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
        ChatChannel.World      => _showWorld,
        ChatChannel.Guild      => _showGuild,
        ChatChannel.Private    => _showPrivate,
        ChatChannel.Nearby     => _showNearby,
        ChatChannel.System     => _showSystem,
        _                      => true,
    };

    private void ToggleFilter(Button button, Color activeColor, ref bool state)
    {
        state = !state;
        ApplyFilterVisual(button, activeColor, state);
        _dirty = true;
    }

    private void ApplyFilterVisual(Button button, Color activeColor, bool isOn)
    {
        if (button == null) return;
        var img = button.image;
        if (img != null) img.color = isOn ? activeColor : FILTER_OFF_COLOR;
    }

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
        ChatChannel.Guild      => "[Guilde]",
        ChatChannel.Private    => "[MP]",
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

            // Raccourci MP — clic sur une ligne avec expéditeur (pas Système) ouvre directement
            // sa conversation dans SocialUI (Florian, 2026-10-05). Toute la ligne est cliquable,
            // pas juste le nom (pas de sous-span cliquable sur un TMP simple sans tag <link>).
            if (!string.IsNullOrEmpty(line.sender))
            {
                string captured = line.sender;
                var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => SocialUI.Instance?.OpenDMWith(captured));
            }
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

        // Canal actuellement sélectionné AVANT reconstruction — sert à retrouver le même canal
        // après coup plutôt que de garder un INDEX brut, qui pointerait vers un canal différent
        // si AetherEcho vient de disparaître/apparaître (décalage de liste). Si ce canal n'existe
        // plus dans la nouvelle liste (AetherEcho consommé), retombe sur World (index 0) au lieu
        // de glisser silencieusement sur le canal suivant (trouvé en revue de code, 2026-10-05).
        ChatChannel? previousChannel = (_pickerChannels.Count > 0 && channelPicker.value < _pickerChannels.Count)
            ? _pickerChannels[channelPicker.value]
            : (ChatChannel?)null;

        _pickerChannels = new List<ChatChannel>(BaseSendableChannels);
        if (ChatSystem.Instance != null && ChatSystem.Instance.HasAetherEcho(_player))
            _pickerChannels.Add(ChatChannel.AetherEcho);

        var labels = new List<string>();
        foreach (var c in _pickerChannels) labels.Add(ChannelPrefix(c));

        int newIndex = previousChannel.HasValue ? _pickerChannels.IndexOf(previousChannel.Value) : -1;
        if (newIndex < 0) newIndex = 0;

        channelPicker.ClearOptions();
        channelPicker.AddOptions(labels);
        channelPicker.value = newIndex;
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
