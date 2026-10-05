using UnityEngine;
using System.Collections.Generic;

// =============================================================
// DIRECTMESSAGESYSTEM — Conversations privées PAR joueur (DM)
// Path : Assets/Scripts/Systems/DirectMessageSystem.cs
// AetherTree GDD v31 — distinct du canal "Privé" du chat global (ChatSystem) : ici une
// conversation séparée par interlocuteur, pas un flux mélangé. Voir UI/PanelSecondaire/SocialUI.cs
// (onglet Chat) et UI/PanelFixe/ChatUI.cs (clic sur un nom = raccourci vers ce système).
//
// Solo aujourd'hui (pas de réseau) — SendDM poste en ÉCHO LOCAL direct dans la conversation
// ciblée, même limite/même raison que ChatSystem.TrySendPlayerMessage (Florian, 2026-10-05).
// Historique NON persisté entre sessions, comme ChatSystem.
// =============================================================

public class DMConversation
{
    public string otherPlayerName;
    public readonly List<ChatLine> messages = new List<ChatLine>();
}

public class DirectMessageSystem : MonoBehaviour
{
    public static DirectMessageSystem Instance { get; private set; }

    private readonly List<DMConversation> _conversations = new List<DMConversation>();

    public IReadOnlyList<DMConversation> GetConversations() => _conversations;

    // =========================================================
    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>Retrouve ou crée la conversation avec ce joueur — jamais null si otherPlayerName
    /// est renseigné. Utilisé par SocialUI.OpenDMWith pour toujours avoir une conversation à
    /// afficher, même la toute première fois qu'on "MP" quelqu'un.</summary>
    public DMConversation GetOrCreateConversation(string otherPlayerName)
    {
        if (string.IsNullOrEmpty(otherPlayerName)) return null;

        foreach (var c in _conversations)
            if (c.otherPlayerName == otherPlayerName) return c;

        var created = new DMConversation { otherPlayerName = otherPlayerName };
        _conversations.Add(created);
        return created;
    }

    /// <summary>Poste un message dans la conversation ciblée — écho local (voir commentaire
    /// d'en-tête). Renvoie false si refusé (texte vide, joueur null).</summary>
    public bool SendDM(string targetPlayerName, string text, Player player)
    {
        if (string.IsNullOrWhiteSpace(text) || player == null || string.IsNullOrEmpty(targetPlayerName))
            return false;

        var conv = GetOrCreateConversation(targetPlayerName);
        conv.messages.Add(new ChatLine { channel = ChatChannel.Private, sender = player.entityName, text = text });

        SocialUI.Instance?.RefreshDMIfOpen(targetPlayerName);
        return true;
    }

    /// <summary>Supprime entièrement la conversation avec ce joueur (fil + entrée dans la liste) —
    /// appelé par SocialUI sur clic du bouton Supprimer. Renvoie false si aucune conversation
    /// n'existait avec ce nom (no-op silencieux côté appelant).</summary>
    public bool DeleteConversation(string otherPlayerName)
    {
        if (string.IsNullOrEmpty(otherPlayerName)) return false;

        for (int i = 0; i < _conversations.Count; i++)
        {
            if (_conversations[i].otherPlayerName == otherPlayerName)
            {
                _conversations.RemoveAt(i);
                return true;
            }
        }
        return false;
    }
}
