using UnityEngine;
using System.Collections.Generic;

// =============================================================
// CHATSYSTEM — Historique de chat + routage des messages par canal
// Path : Assets/Scripts/Systems/ChatSystem.cs
// AetherTree GDD v31 — voir docs/superpowers/specs/2026-10-05-chat-system-design.md
//
// Solo aujourd'hui (pas de réseau, voir a implémenter/AetherTree_Recap_Reseau+refactor.md) —
// Private/Nearby/AetherEcho postent en ÉCHO LOCAL direct (TrySendPlayerMessage ajoute la
// ligne immédiatement, comme si envoyée-et-reçue). Le jour où Mirror existe, TrySendPlayerMessage
// est le point d'ancrage à transformer en [Command] vers le serveur — ne PAS appeler AddLine()
// directement depuis un futur code réseau sans passer par ce point.
//
// Guild : toujours refusé, aucun GuildSystem n'existe (voir Data/PNJ/PNJData.cs, commentaire sur
// le retrait de Mayor/TryCreateGuild le 2026-10-04).
//
// PAS de ChatChannel.World (retiré, 2026-10-05, Florian) — "Monde" n'est plus un canal d'envoi,
// juste un MODE D'AFFICHAGE côté ChatUI (agrégat Alentour+Guilde+Privé+Écho, Système toujours à
// part) — voir ChatUI.cs. Aucun message n'est jamais tagué World ici.
//
// ChatChannel.Private RESTE (filtre "Privé" affiche tous les messages privés dans le flux
// unifié, Florian 2026-10-05) — ce qui a changé, c'est juste l'ENVOI : plus de "Privé" comme
// option du picker (un message privé sans destinataire n'avait pas de sens), remplacé par le
// raccourci "/NomDuJoueur message" tapé directement dans la saisie (façon Nostale) — voir
// ChatUI.OnSendClicked. Poste ici (tagué Private, visible/filtrable) ET dans
// DirectMessageSystem (conversation dédiée par joueur, visible dans SocialUI), pas l'un OU
// l'autre.
// =============================================================

public enum ChatChannel { Guild, Private, Nearby, System, AetherEcho }

public class ChatLine
{
    public ChatChannel   channel;
    public string        sender;    // "" pour une ligne Système
    public string        text;
    public System.DateTime timestamp;
    public string         recipient; // UNIQUEMENT pour Private — à qui le message est adressé,
                                      // voir ChatUI.Refresh ("À X" si sender == nous, "De X" sinon).
}

public class ChatSystem : MonoBehaviour
{
    public static ChatSystem Instance { get; private set; }

    private const int MAX_HISTORY = 200;

    private readonly List<ChatLine> _history = new List<ChatLine>();

    public IReadOnlyList<ChatLine> GetHistory() => _history;

    // =========================================================
    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()  => Subscribe();
    private void OnDisable() => Unsubscribe();

    // GameEventBus.Reset() (changement de scène) désabonne tout — sans Resubscribe() listé dans
    // Reset(), ChatSystem perdrait ses abonnements Système au premier changement de scène de la
    // session, même bug que QuestSystem/QuestTrackerUI/QuestJournalUI/PNJQuestBoardUI déjà corrigé
    // 4 fois cette session (ne pas le refaire une 5e).
    public void Resubscribe() { Unsubscribe(); Subscribe(); }

    private void Subscribe()
    {
        GameEventBus.OnMobKilled      += HandleMobKilled;
        GameEventBus.OnItemAction     += HandleItemAction;
        GameEventBus.OnQuestAction    += HandleQuestAction;
        GameEventBus.OnPlayerLevelUp  += HandlePlayerLevelUp;
        GameEventBus.OnMailReceived   += HandleMailReceived;
    }

    private void Unsubscribe()
    {
        GameEventBus.OnMobKilled      -= HandleMobKilled;
        GameEventBus.OnItemAction     -= HandleItemAction;
        GameEventBus.OnQuestAction    -= HandleQuestAction;
        GameEventBus.OnPlayerLevelUp  -= HandlePlayerLevelUp;
        GameEventBus.OnMailReceived   -= HandleMailReceived;
    }

    // HandleMobKilled reste un no-op volontaire — MobKilledEvent ne porte aucune info de loot
    // par item, voir HandleItemAction ci-dessous pour le vrai point d'accroche loot (décision
    // prise en lisant Events/GameEvents.cs + Systems/LootManager.cs, pas devinée).
    private void HandleMobKilled(MobKilledEvent e) { }

    private void HandleItemAction(ItemEvent e)
    {
        if (e.action != ItemAction.Pickup) return;
        string name = !string.IsNullOrEmpty(e.itemName) ? e.itemName : e.itemID;
        PostSystemMessage($"Loot : {name} ×{e.quantity}");
    }

    private void HandleQuestAction(QuestEvent e)
    {
        if (e.action != QuestAction.TurnedIn || e.quest == null) return;
        PostSystemMessage($"Quête terminée : {e.quest.questName}");
    }

    private void HandlePlayerLevelUp(PlayerLevelUpEvent e)
    {
        PostSystemMessage($"Niveau {e.newLevel} atteint !");
    }

    private void HandleMailReceived(MailReceivedEvent e)
    {
        PostSystemMessage($"Nouveau mail : {e.subject}");
    }

    // =========================================================
    // ENVOI JOUEUR
    // =========================================================

    /// <summary>Appelé par ChatUI quand le joueur valide sa saisie. Renvoie false si refusé
    /// (Guild sans GuildSystem, AetherEcho sans item en stock OU disparu entre la sélection du
    /// canal et l'envoi). System n'est JAMAIS un canal d'envoi valide ici — lecture seule, voir
    /// PostSystemMessage. `recipient` n'a de sens QUE pour ChatChannel.Private (nom du
    /// destinataire, voir ChatUI.OnSendClicked "/NomDuJoueur message") — ignoré pour tout autre
    /// canal.</summary>
    public bool TrySendPlayerMessage(ChatChannel channel, string text, Player player, string recipient = null)
    {
        if (string.IsNullOrWhiteSpace(text) || player == null) return false;

        switch (channel)
        {
            case ChatChannel.System:
                return false; // jamais un canal d'envoi joueur

            case ChatChannel.Guild:
                PostSystemMessage("Pas de guilde.");
                return false;

            case ChatChannel.AetherEcho:
            {
                // Re-check défensif ICI (pas seulement à l'ouverture du picker) — évite une
                // double-consommation si le joueur double-clique Envoyer avant que l'UI ne se
                // rafraîchisse (Review Focus de la spec, 2026-10-05).
                var echoItem = FindAetherEchoData();
                if (echoItem == null) return false;
                if (InventorySystem.Instance == null || !InventorySystem.Instance.ConsumeItem(echoItem, 1))
                    return false;

                AddLine(ChatChannel.AetherEcho, player.entityName, text);
                return true;
            }

            case ChatChannel.Private:
                AddLine(ChatChannel.Private, player.entityName, text, recipient);
                return true;

            default: // Nearby — écho local direct (voir commentaire d'en-tête)
                AddLine(channel, player.entityName, text);
                return true;
        }
    }

    /// <summary>True si le joueur possède au moins 1 item ConsumableType.AetherEcho — utilisé par
    /// ChatUI pour savoir si "Écho d'Aether" doit apparaître dans le picker de canal d'envoi.
    /// Paramètre player non utilisé aujourd'hui (InventorySystem est un singleton global, pas par
    /// joueur, voir limitation solo documentée ailleurs ce chantier) — gardé pour cohérence de
    /// signature avec le reste de l'API et la lisibilité d'un futur passage multijoueur.</summary>
    public bool HasAetherEcho(Player player) => FindAetherEchoData() != null;

    /// <summary>Premier ConsumableData possédé avec consumableType == AetherEcho — scan
    /// dynamique, pas de référence SO fixe (supporte plusieurs variantes futures sans
    /// reconfigurer ChatSystem, voir spec).</summary>
    private ConsumableData FindAetherEchoData()
    {
        if (InventorySystem.Instance == null) return null;
        foreach (var item in InventorySystem.Instance.GetAllItems())
        {
            var data = item.ConsumableInstance?.data;
            if (data != null && data.consumableType == ConsumableType.AetherEcho)
                return data;
        }
        return null;
    }

    // =========================================================
    // HISTORIQUE
    // =========================================================

    private void AddLine(ChatChannel channel, string sender, string text, string recipient = null)
    {
        var now = System.DateTime.Now;
        _history.Add(new ChatLine { channel = channel, sender = sender, text = text, timestamp = now, recipient = recipient });
        if (_history.Count > MAX_HISTORY) _history.RemoveAt(0);

        GameEventBus.Publish(new ChatMessageEvent { channel = channel, sender = sender, text = text, timestamp = now, recipient = recipient });
    }

    private void PostSystemMessage(string text) => AddLine(ChatChannel.System, "", text);
}
