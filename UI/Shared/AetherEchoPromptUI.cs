using UnityEngine;
using UnityEngine.UI;
using TMPro;

// =============================================================
// AETHERECHOPROMPTUI — Panel de saisie pour l'Écho d'Aether
// Path : Assets/Scripts/UI/Shared/AetherEchoPromptUI.cs
// AetherTree GDD v31 — voir docs/superpowers/specs/2026-10-05-chat-system-design.md
//
// Ouvert par ConsoBarUI.UseConsumable (branche ConsumableType.AetherEcho) — PAS lié à l'ouverture
// de ChatUI, fonctionne même si la fenêtre de chat n'est pas affichée. Annuler ne consomme RIEN —
// seul Valider appelle ChatSystem.TrySendPlayerMessage, qui consomme l'item lui-même au moment de
// l'envoi réel (voir Systems/ChatSystem.cs — ne PAS dupliquer de logique de consommation ici).
//
// SETUP HIERARCHY attendu (Florian construit, même convention que UI/Shared/ConfirmationUI.cs) :
// Ce script (AetherEchoPromptUI) doit être posé sur un GameObject TOUJOURS ACTIF — sinon Awake()
// ne tourne jamais, Instance reste null, et ConsoBarUI.UseConsumable n'ouvre plus rien SILENCIEUSEMENT
// (utiliser l'item ne fait plus rien, aucune erreur visible). Seul le champ `panel` (son enfant) est
// inactif par défaut et togglé par Open()/Close() — exactement comme ConfirmationUI.cs.
//
// AetherEchoScript (GameObject TOUJOURS ACTIF — ce script est dessus)
//   └── AetherEchoPromptPanel (assigner sur `panel`, inactif par défaut)
//         ├── MessageInput        (TMP_InputField)
//         ├── ValidateButton      (Button)
//         └── CancelButton        (Button)
// =============================================================

public class AetherEchoPromptUI : MonoBehaviour
{
    public static AetherEchoPromptUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject    panel;
    public TMP_InputField messageInput;
    public Button          validateButton;
    public Button          cancelButton;

    private Player _player;

    // =========================================================
    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        validateButton?.onClick.AddListener(OnValidateClicked);
        cancelButton  ?.onClick.AddListener(Close);
        messageInput  ?.onSubmit.AddListener(_ => OnValidateClicked()); // Entrée envoie aussi
        Close();
    }

    public void Open(Player player)
    {
        _player = player;
        if (messageInput != null) messageInput.text = "";
        if (panel != null) panel.SetActive(true);
    }

    private void OnValidateClicked()
    {
        if (messageInput == null || string.IsNullOrWhiteSpace(messageInput.text)) return;
        if (_player == null || ChatSystem.Instance == null) return;

        bool sent = ChatSystem.Instance.TrySendPlayerMessage(ChatChannel.AetherEcho, messageInput.text, _player);
        if (sent) Close();
        // Refusé (item disparu entre l'ouverture et le clic, ex: vendu entre-temps) : panel reste
        // ouvert, le joueur garde son texte tapé, peut réessayer ou Annuler.
    }

    private void Close()
    {
        if (panel != null) panel.SetActive(false);
    }
}
