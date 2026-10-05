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
// FilterBar — PAS 5 toggles indépendants : un sélecteur à choix UNIQUE (comme des tabs/radio),
// géré avec de simples Button (pas de Toggle construit côté Editor). "Monde" N'EST PLUS un vrai
// canal d'envoi (voir ChatSystem.ChatChannel, World retiré 2026-10-05) — c'est juste le mode
// d'affichage AGRÉGÉ : Alentour + Guilde + Privé réunis (+ Écho d'Aether, qui ignore TOUJOURS le
// mode choisi, voir plus bas). Système reste TOUJOURS à part, jamais inclus dans l'agrégat Monde
// — seul son propre bouton l'affiche (Florian, 2026-10-05). Cliquer Alentour/Guilde/Privé/Système
// isole CE SEUL canal (le reste disparaît de la vue, mais pas de l'historique ni de l'envoi).
//
// Écho d'Aether IGNORE TOUJOURS le mode de filtre choisi (aucun bouton dédié) — c'est sa raison
// d'être, voir spec section "Nostale : le haut-parleur ignore le filtre de vue temporaire".
//
// SETUP HIERARCHY attendu (Florian construit, ce script fait les Find()/assignations en Inspector
// comme pour les autres UI de cette session) :
//
// ChatPanel (racine, toujours active — HUD permanent)
//   ├── MessageScrollView            (ScrollRect — assigner à messageScrollRect, pour l'auto-scroll)
//   │     └── Viewport > Content     (Transform — parent des lignes instanciées, assigner à
//   │                                messageContent)
//   │                                Content a besoin d'un Vertical Layout Group (Child Force
//   │                                Expand Height DÉCOCHÉ, Control Child Size Height COCHÉ) +
//   │                                Content Size Fitter (Vertical Fit = Preferred Size), sinon
//   │                                les lignes se resserrent au lieu de garder leur taille.
//   ├── FilterBar (sélecteur à choix unique, PAS 5 toggles indépendants)
//   │     ├── FilterWorld           (Button — "Monde" = vue agrégée Alentour+Guilde+Privé+Écho)
//   │     ├── FilterGuild           (Button — isole Guilde seul)
//   │     ├── FilterPrivate         (Button — isole Privé seul)
//   │     ├── FilterNearby          (Button — isole Alentour seul)
//   │     └── FilterSystem          (Button — isole Système seul, jamais dans l'agrégat Monde)
//   ├── InputBar
//   │     ├── ChannelPicker          (TMP_Dropdown — Guilde/Privé/Alentour + Écho d'Aether si
//   │     │                           possédé ; PAS de "Monde", plus un canal d'envoi réel)
//   │     ├── InputField             (TMP_InputField)
//   │     └── SendButton             (Button)
//   └── ChatLinePrefab (prefab séparé, pas un enfant actif — glissé en Inspector) — a besoin
//       d'un Content Size Fitter (Vertical Fit = Preferred Size) sur sa racine pour que le
//       Vertical Layout Group du Content lise sa vraie hauteur au lieu d'une taille fixe.
//         ├── (TextMeshProUGUI principal, n'importe où dans le prefab)
//         └── Time (optionnel — TextMeshProUGUI nommé "Time", affiche l'heure HH:mm à part ;
//                    absent = l'heure est juste préfixée dans le texte principal à la place)
// =============================================================

public class ChatUI : MonoBehaviour
{
    public static ChatUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject panel;

    [Header("Historique")]
    public ScrollRect messageScrollRect; // pour l'auto-scroll vers le bas (MessageScrollView)
    public Transform  messageContent;
    public GameObject chatLinePrefab;

    [Tooltip("Nombre max de lignes INSTANCIÉES à l'écran en même temps (après filtre) — distinct " +
             "du cap de 200 côté ChatSystem (qui limite le STOCKAGE). Rebuild complet à chaque " +
             "nouveau message, donc garder ce nombre raisonnable évite de recréer des dizaines de " +
             "GameObjects inutiles à chaque frappe.")]
    public int maxRenderedLines = 50;

    [Header("Filtres — sélecteur à choix unique (Button, pas Toggle)")]
    public Button filterWorld;
    public Button filterGuild;
    public Button filterPrivate;
    public Button filterNearby;
    public Button filterSystem;

    private enum FilterMode { Aggregate, Nearby, Guild, Private, System }
    private FilterMode _filterMode = FilterMode.Aggregate;
    private static readonly Color FILTER_OFF_COLOR = new Color(0.3f, 0.3f, 0.35f, 0.5f);

    [Header("Couleurs par canal (World = teinte du bouton \"Monde\" / vue agrégée uniquement, pas un canal de message)")]
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
    // dynamiquement en plus de ceux-ci (voir RefreshChannelPicker). Nearby EN PREMIER (pas Guild) :
    // Guild refuse TOUJOURS (pas de GuildSystem) et poste sa ligne de refus en Système, invisible
    // tant que la vue n'est pas explicitement sur le filtre Système — avec Guild par défaut
    // (index 0), le tout premier clic sur Envoyer semblait ne rien faire (bug trouvé en lisant le
    // code après le rapport de Florian, 2026-10-05 : "le bouton envoyer n'envoie pas").
    //
    // PAS de ChatChannel.Private ici — un message privé SANS destinataire n'a pas de sens, le
    // picker ne peut pas en fournir un. Le seul moyen d'envoyer un privé est le raccourci
    // "/NomDuJoueur message" tapé directement (voir OnSendClicked) — le filtre "Privé" de la
    // FilterBar reste lui fonctionnel, il affiche juste ce que ce raccourci poste.
    private static readonly ChatChannel[] BaseSendableChannels =
        { ChatChannel.Nearby, ChatChannel.Guild };

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

        filterWorld  ?.onClick.AddListener(() => SetFilterMode(FilterMode.Aggregate));
        filterGuild  ?.onClick.AddListener(() => SetFilterMode(FilterMode.Guild));
        filterPrivate?.onClick.AddListener(() => SetFilterMode(FilterMode.Private));
        filterNearby ?.onClick.AddListener(() => SetFilterMode(FilterMode.Nearby));
        filterSystem ?.onClick.AddListener(() => SetFilterMode(FilterMode.System));

        RefreshFilterVisuals(); // état initial : Monde (agrégat) actif par défaut

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

    /// <summary>"Monde" = vue agrégée Alentour+Guilde+Privé (pas un vrai canal, voir en-tête) —
    /// Système n'y est JAMAIS inclus, seul son propre mode l'affiche. Écho d'Aether ignore
    /// TOUJOURS le mode choisi, peu importe lequel.</summary>
    private bool IsChannelVisible(ChatChannel channel)
    {
        if (channel == ChatChannel.AetherEcho) return true;

        return _filterMode switch
        {
            FilterMode.Aggregate => channel == ChatChannel.Nearby || channel == ChatChannel.Guild || channel == ChatChannel.Private,
            FilterMode.Nearby    => channel == ChatChannel.Nearby,
            FilterMode.Guild     => channel == ChatChannel.Guild,
            FilterMode.Private   => channel == ChatChannel.Private,
            FilterMode.System    => channel == ChatChannel.System,
            _                    => true,
        };
    }

    private void SetFilterMode(FilterMode mode)
    {
        _filterMode = mode;
        RefreshFilterVisuals();
        _dirty = true;
    }

    /// <summary>Un seul bouton actif à la fois (sélecteur à choix unique) — pas d'état "on/off"
    /// indépendant par canal comme un vrai Toggle, juste lequel des 5 correspond au mode courant.</summary>
    private void RefreshFilterVisuals()
    {
        ApplyFilterVisual(filterWorld,   colorWorld,   _filterMode == FilterMode.Aggregate);
        ApplyFilterVisual(filterNearby,  colorNearby,  _filterMode == FilterMode.Nearby);
        ApplyFilterVisual(filterGuild,   colorGuild,   _filterMode == FilterMode.Guild);
        ApplyFilterVisual(filterPrivate, colorPrivate, _filterMode == FilterMode.Private);
        ApplyFilterVisual(filterSystem,  colorSystem,  _filterMode == FilterMode.System);
    }

    private void ApplyFilterVisual(Button button, Color activeColor, bool isOn)
    {
        if (button == null) return;
        var img = button.image;
        if (img != null) img.color = isOn ? activeColor : FILTER_OFF_COLOR;
    }

    private Color ChannelColor(ChatChannel channel) => channel switch
    {
        ChatChannel.Guild      => colorGuild,
        ChatChannel.Private    => colorPrivate,
        ChatChannel.Nearby     => colorNearby,
        ChatChannel.System     => colorSystem,
        ChatChannel.AetherEcho => colorAetherEcho,
        _                      => Color.white,
    };

    private string ChannelPrefix(ChatChannel channel) => channel switch
    {
        ChatChannel.Guild      => "[Guilde]",
        ChatChannel.Private    => "[MP]",
        ChatChannel.Nearby     => "[Alentour]",
        ChatChannel.System     => "[Système]",
        ChatChannel.AetherEcho => "[Écho d'Aether]",
        _                      => "",
    };

    /// <summary>Une ligne Private affiche "À X" (nous avons envoyé) ou "De X" (reçu — pas encore
    /// possible en solo, mais le bon comportement une fois le réseau là) au lieu du nom brut de
    /// l'expéditeur — voir ChatLine.recipient. Tous les autres canaux affichent juste le nom de
    /// l'expéditeur comme avant (Florian, 2026-10-05 : "le MP doit afficher à/de destinataire,
    /// pas notre propre nom").</summary>
    private string BuildSenderLabel(ChatLine line)
    {
        if (line.channel == ChatChannel.Private && !string.IsNullOrEmpty(line.recipient))
        {
            bool isOutgoing = _player != null && line.sender == _player.entityName;
            return isOutgoing ? $"À {line.recipient} : " : $"De {line.sender} : ";
        }

        return string.IsNullOrEmpty(line.sender) ? "" : $"{line.sender} : ";
    }

    private void Refresh()
    {
        foreach (var go in _lineObjects)
            if (go != null) Destroy(go);
        _lineObjects.Clear();

        if (ChatSystem.Instance == null || messageContent == null || chatLinePrefab == null) return;
        if (_player == null) _player = FindObjectOfType<Player>();

        // Filtre d'abord, NE GARDE que les maxRenderedLines plus récentes — évite d'instancier
        // jusqu'à 200 GameObjects (le cap de stockage de ChatSystem) à chaque nouveau message, et
        // empêche l'affichage de surcharger visuellement (Florian, 2026-10-05).
        var matching = new List<ChatLine>();
        foreach (var line in ChatSystem.Instance.GetHistory())
            if (IsChannelVisible(line.channel)) matching.Add(line);

        int startIndex = Mathf.Max(0, matching.Count - maxRenderedLines);

        for (int i = startIndex; i < matching.Count; i++)
        {
            var line = matching[i];

            var go = Instantiate(chatLinePrefab, messageContent);
            _lineObjects.Add(go);

            string timeStr = line.timestamp.ToString("HH:mm");

            // Champ "Time" dédié optionnel (enfant nommé "Time" avec un TextMeshProUGUI) — si
            // absent, l'heure est juste préfixée dans le texte principal (fonctionne sans rien
            // construire de plus côté Hierarchy). Cherche le texte PRINCIPAL en excluant
            // explicitement "Time" (sinon GetComponentInChildren pourrait retomber dessus en
            // premier selon l'ordre des enfants dans le prefab, et écraser l'heure avec le message).
            var timeTxt = go.transform.Find("Time")?.GetComponent<TextMeshProUGUI>();
            if (timeTxt != null) timeTxt.text = timeStr;

            TextMeshProUGUI txt = null;
            foreach (var t in go.GetComponentsInChildren<TextMeshProUGUI>())
            {
                if (t == timeTxt) continue;
                txt = t;
                break;
            }
            if (txt == null) continue; // prefab mal construit — aucun TMP principal trouvé

            string sender = BuildSenderLabel(line);
            string timePrefix = timeTxt == null ? $"{timeStr} " : ""; // déjà affiché à part sinon
            txt.text  = $"{timePrefix}{ChannelPrefix(line.channel)} {sender}{line.text}";
            txt.color = ChannelColor(line.channel);

            // Raccourci MP — clic sur une ligne avec expéditeur (pas Système) ouvre directement
            // sa conversation dans SocialUI (Florian, 2026-10-05). Toute la ligne est cliquable,
            // pas juste le nom (pas de sous-span cliquable sur un TMP simple sans tag <link>).
            // Pour une ligne Private SORTANTE (nous l'avons envoyée), la conversation à ouvrir est
            // celle du DESTINATAIRE, pas la nôtre — sinon clic = ouvrir un MP avec soi-même.
            bool isOutgoingPrivate = line.channel == ChatChannel.Private
                && !string.IsNullOrEmpty(line.recipient)
                && _player != null && line.sender == _player.entityName;
            string dmTarget = isOutgoingPrivate ? line.recipient : line.sender;

            if (!string.IsNullOrEmpty(dmTarget))
            {
                string captured = dmTarget;
                var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => SocialUI.Instance?.OpenDMWith(captured));
            }
        }

        ScrollToBottom();
    }

    /// <summary>Force le ScrollRect tout en bas après un rebuild — sans le ForceUpdateCanvases
    /// préalable, le Content n'a pas encore sa taille recalculée (ContentSizeFitter) au moment où
    /// on lit/écrit verticalNormalizedPosition, et le scroll partirait de l'ancienne taille
    /// (Florian, 2026-10-05 : "le chat doit défiler pour avoir le dernier message en bas").
    /// 0 = bas, 1 = haut pour un ScrollRect vertical standard Unity.</summary>
    private void ScrollToBottom()
    {
        if (messageScrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        messageScrollRect.verticalNormalizedPosition = 0f;
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

        string text = inputField.text;

        // Raccourci "/NomDuJoueur message" (façon Nostale "/Nom Texte") — SEUL moyen d'envoyer un
        // privé, indépendamment du canal sélectionné dans le picker (qui ne propose plus Private,
        // voir BaseSendableChannels). Poste dans le flux unifié (tagué Private, filtrable via le
        // bouton "Privé") ET dans DirectMessageSystem (conversation dédiée, visible dans
        // SocialUI) — les deux, pas l'un ou l'autre (Florian, 2026-10-05).
        if (text.StartsWith("/"))
        {
            int spaceIndex = text.IndexOf(' ');
            if (spaceIndex > 1 && spaceIndex < text.Length - 1)
            {
                string targetName = text.Substring(1, spaceIndex - 1);
                string dmText     = text.Substring(spaceIndex + 1);

                bool sentToFeed = ChatSystem.Instance != null
                    && ChatSystem.Instance.TrySendPlayerMessage(ChatChannel.Private, dmText, _player, targetName);
                DirectMessageSystem.Instance?.SendDM(targetName, dmText, _player);

                if (sentToFeed) inputField.text = "";
                return;
            }
        }

        ChatChannel channel = ChatChannel.Nearby; // défaut si picker vide/absent
        if (channelPicker != null && _pickerChannels.Count > 0)
        {
            int index = Mathf.Clamp(channelPicker.value, 0, _pickerChannels.Count - 1);
            channel = _pickerChannels[index];
        }

        bool sent = ChatSystem.Instance != null && ChatSystem.Instance.TrySendPlayerMessage(channel, text, _player);
        if (sent) inputField.text = "";

        // Le dernier Écho d'Aether vient peut-être d'être consommé — re-synchronise le picker
        // tout de suite plutôt que d'attendre la prochaine ouverture.
        RefreshChannelPicker();
    }
}
