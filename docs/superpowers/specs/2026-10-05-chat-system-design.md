# Chat System — Design

**Date:** 2026-10-05
**Statut:** Approuvé par Florian (conversation), prêt pour writing-plans.

## Contexte

AetherTree n'a aucun système de chat. Florian veut un chat avec plusieurs canaux
(World/Guild/Private/Alentour/Système) plus un item spécial "Écho d'Aether" (inspiré du
haut-parleur de Nostale) qui diffuse un message visible par tous les joueurs peu importe où ils
sont.

**Contrainte majeure, vérifiée en code avant de designer** : AetherTree est **solo aujourd'hui**,
aucun réseau n'existe (`a implémenter/AetherTree_Recap_Reseau+refactor.md` — Mirror est prévu,
pas câblé). Les canaux World/Guild/Private/Alentour n'ont donc personne à qui parler pour
l'instant. Florian a choisi explicitement de construire l'UI complète des 5 canaux maintenant
quand même (pas d'attendre le réseau), en acceptant que World/Private/Alentour/Écho d'Aether
fonctionnent en **écho local** (le message tapé s'affiche directement, comme envoyé-et-reçu) en
attendant Mirror.

Recherche Nostale faite avant de verrouiller le design (page officielle
`gameguide.nostale.fr/main/game_interface`, récupérée 2026-10-05) :
- Nostale a **UNE seule fenêtre de tchat**, pas des onglets qui cachent les autres canaux — un
  filtre de CATÉGORIE en haut de la fenêtre contrôle ce qui s'affiche.
- Canaux d'envoi : Chuchoter (`/Nom Texte`), Groupe/Raid (`;Texte`), Famille (`:Texte`),
  Espace-temps (`!Texte`).
- Le haut-parleur est un vrai mécanisme (les Options permettent de bloquer "les invitations de
  chuchotement et de haut-parleur") — bloqué via un réglage PERMANENT dans les Options, pas via
  le filtre de catégorie temporaire de la fenêtre. Cette nuance est respectée ci-dessous : l'Écho
  d'Aether ignore le filtre de vue temporaire, un blocage permanent façon Options n'est PAS
  construit dans cette v1 (hors scope, pas demandé).

## Architecture

### Vue d'ensemble

```
ChatUI (HUD permanent, dock bas-gauche)
   │  lit l'historique + s'abonne à GameEventBus.OnChatMessage
   ▼
ChatSystem (singleton, Systems/ChatSystem.cs)
   │  publie GameEventBus.OnChatMessage à chaque nouvelle ligne
   │  s'abonne à OnMobKilled / OnItemAction / OnQuestAction / OnPlayerLevelUp / OnMailReceived
   ▼
GameEventBus (existant)
   ▲
   │  nouveau : OnMailReceived, publié par MailboxSystem
```

### `ChatChannel` (enum, défini dans `Systems/ChatSystem.cs`)

```csharp
public enum ChatChannel { World, Guild, Private, Nearby, System, AetherEcho }
```

### `Systems/ChatSystem.cs` (nouveau singleton)

Mêmes conventions que les singletons déjà construits cette session (QuestSystem,
QuestTrackerUI) : `Instance`, `Subscribe()/Unsubscribe()/Resubscribe()` enregistré dans
`GameEventBus.Reset()` (sinon perd ses abonnements au premier changement de scène — bug déjà
vécu et corrigé 2 fois cette session, ne pas le répéter).

```csharp
public class ChatLine
{
    public ChatChannel channel;
    public string      sender;   // nom affiché, "" pour Système
    public string      text;
}

public class ChatSystem : MonoBehaviour
{
    public static ChatSystem Instance { get; private set; }

    private const int MAX_HISTORY = 200; // capé, les plus anciennes lignes tombent

    private readonly List<ChatLine> _history = new List<ChatLine>();

    public IReadOnlyList<ChatLine> GetHistory() => _history;

    /// Appelé par ChatUI quand le joueur valide sa saisie. Renvoie false si refusé
    /// (Guild sans GuildSystem, AetherEcho sans item en stock).
    public bool TrySendPlayerMessage(ChatChannel channel, string text, Player player);

    /// Pour le picker de canal d'envoi — Écho d'Aether n'apparaît que si true.
    public bool HasAetherEcho(Player player);
}
```

**`TrySendPlayerMessage` — logique par canal :**
- `Guild` → toujours refusé (pas de GuildSystem) ; poste une ligne `System` "Pas de guilde."
  dans l'historique (visible, pas un refus silencieux) ; renvoie false.
- `AetherEcho` → si `!HasAetherEcho(player)`, refuse (garde défensive — le picker ne doit de
  toute façon jamais proposer ce canal sans stock). Sinon : `InventorySystem.Instance
  .ConsumeItem(item, 1)` D'ABORD (le premier `ConsumableData` possédé avec
  `consumableType == AetherEcho`, scanné dynamiquement — pas de référence SO fixe, pour
  supporter plusieurs variantes futures sans reconfigurer `ChatSystem`), PUIS poste la ligne. Si
  jamais `ConsumeItem` échoue (item disparu entre la sélection du canal et l'envoi — vendu,
  détruit), refuse silencieusement comme les autres précheck de ce projet (pas de perte de
  message, pas de crash).
- `World` / `Private` / `Nearby` → poste directement (écho local, `sender = player.entityName`).
  **Commentaire de code explicite** marquant ceci comme le point d'ancrage réseau futur : le jour
  où Mirror existe, ce point devient une `[Command]` vers le serveur au lieu d'un
  `AddLine()` direct.
- `System` → jamais dans ce chemin (pas sélectionnable à l'envoi, lecture seule — voir
  `PostSystemMessage` séparé ci-dessous, appelé par les handlers d'événements, pas par l'UI).

**Internes :**
```csharp
private void AddLine(ChatChannel channel, string sender, string text)
{
    _history.Add(new ChatLine { channel = channel, sender = sender, text = text });
    if (_history.Count > MAX_HISTORY) _history.RemoveAt(0);
    GameEventBus.Publish(new ChatMessageEvent { channel = channel, sender = sender, text = text });
}

private void PostSystemMessage(string text) => AddLine(ChatChannel.System, "", text);
```

**Handlers Système (mêmes conventions que QuestSystem — un handler privé par event, abonné dans
`Subscribe()`) :**
- `OnMobKilled` → si le mob a droppé du loot pour ce joueur (même info que `LootManager`
  distribue déjà) : `"Loot : {item} ×{quantité}"`. **Point d'intégration à vérifier en tâche de
  plan** : `MobKilledEvent` actuel ne porte peut-être pas directement la liste des items
  obtenus — lire `LootManager.cs`/`MobKilledEvent` avant d'écrire ce handler, adapter le texte à
  ce qui est réellement disponible sur l'event plutôt que de deviner un champ qui n'existe pas.
- `OnQuestAction` → `TurnedIn` → `"Quête terminée : {questName}"`. Autres `QuestAction` (Abandoned/
  Failed) volontairement IGNORÉS ici (pas demandé, évite le bruit).
- `OnPlayerLevelUp` → `"Niveau {level} atteint !"`.
- `OnMailReceived` (nouveau, voir ci-dessous) → `"Nouveau mail : {subject}"`.

### `Events/GameEvents.cs` — nouveaux events

```csharp
public struct ChatMessageEvent
{
    public ChatChannel channel;
    public string       sender;
    public string       text;
}

public struct MailReceivedEvent
{
    public string subject;
}
```

`Events/GameEventBus.cs` gagne :
```csharp
public static event Action<ChatMessageEvent>  OnChatMessage;
public static event Action<MailReceivedEvent> OnMailReceived;
```
+ `ChatSystem.Instance?.Resubscribe();` ajouté à la liste dans `Reset()`.

### `Systems/MailboxSystem.cs` — modification

Actuellement AUCUN event publié (vérifié par lecture — uniquement des `Debug.Log`). Ajouter
`GameEventBus.Publish(new MailReceivedEvent { subject = mail.subject });` au point où un nouveau
mail est effectivement ajouté à la boîte du joueur (trouver ce point exact en tâche de plan — lire
la méthode d'envoi/réception réelle, ne pas deviner son nom).

### `Data/Inventory/ConsumableData.cs` — modification

```csharp
public enum ConsumableType
{
    Potion       = 0,
    Food         = 1,
    DungeonKey   = 2,
    TeleportItem = 3,
    Other        = 4,
    RewardChest  = 5,
    AetherEcho   = 6, // nouveau, append-only — voir ChatSystem
}
```

**`ConsoBarUI.UseConsumable` — branche dédiée, PAS le chemin effet-instantané des autres types** :
contrairement à Potion/Food/etc., l'Écho d'Aether n'a pas d'effet tant que le joueur n'a pas
réellement envoyé un message — le consommer au moment du "clic d'usage" normal serait faux (le
joueur pourrait annuler sans rien taper). Décision finale (Florian, 2026-10-05) :

- Utiliser un Écho d'Aether (ConsoBar OU double-clic inventaire — les deux passent déjà par
  `UseConsumable`) ouvre un **petit panel dédié** (`UI/Shared/AetherEchoPromptUI.cs`, même
  famille que `ConfirmationUI` déjà présent côté Abandon de quête) : un champ de saisie + 2
  boutons, **Valider** (envoie réellement via `ChatSystem.TrySendPlayerMessage(AetherEcho, texte,
  player)` — qui consomme l'item à ce moment précis, pas avant) et **Annuler** (ferme le panel,
  rien n'est touché — ni l'item ni l'inventaire).
- `UseConsumable` lui-même ne consomme RIEN pour `AetherEcho` — il se contente d'ouvrir le panel
  et de `return` immédiatement, exactement comme s'il s'agissait d'un type "pas encore implémenté"
  côté effet instantané (parce que ça en est un, volontairement).
- Le panel ne connaît pas d'instance spécifique à consommer — il délègue entièrement à
  `ChatSystem.TrySendPlayerMessage`, qui re-scanne l'inventaire lui-même (`FindAetherEchoData`,
  déjà spécifié plus haut) au moment de l'envoi. Pas de risque de consommer un exemplaire
  "périmé" si l'inventaire a changé entre l'ouverture du panel et le clic Valider.
- Ce même panel n'est PAS lié à l'ouverture de `ChatUI` — fonctionne même si la fenêtre de chat
  n'est pas affichée à l'écran (`ChatSystem.TrySendPlayerMessage` ne dépend d'aucun état UI).

### `UI/PanelFixe/ChatUI.cs` (nouveau, HUD permanent — Florian construit la Hierarchy/prefabs,
le script fait les `Find()` comme pour les autres UI de cette session)

- Dock toujours visible, PAS un panel à toggle.
- Un flux UNIQUE (une ligne par `ChatLine`), couleur/préfixe par canal (ex: `[Monde]` jaune,
  `[Famille]` vert, `[Chuchotement]` rose, `[Alentour]` blanc, `[Système]` gris, `[Écho d'Aether]`
  couleur distincte/proéminente — Florian choisit les couleurs exactes en Inspector).
- Toggles de filtre par canal en haut (checkbox/bouton par `ChatChannel`, sauf `AetherEcho` qui
  n'en a PAS — il ignore TOUJOURS le filtre, toujours affiché, voir contexte Nostale ci-dessus).
  Par défaut tous les canaux affichés.
- Sélecteur de canal d'ENVOI à côté de la saisie — liste fixe World/Guild/Private/Nearby,
  PLUS `AetherEcho` ajouté dynamiquement seulement si `ChatSystem.Instance.HasAetherEcho(player)`
  (recalculé à chaque fois le picker s'ouvre, ou sur un refresh léger — pas besoin d'un event
  dédié, un check à l'ouverture du picker suffit).
- `System` n'apparaît JAMAIS dans le sélecteur d'envoi (lecture seule).
- Abonné à `GameEventBus.OnChatMessage` (+ `Resubscribe()` propre dans `GameEventBus.Reset()`,
  même pattern que les 3 UI de quête) pour ajouter une ligne en live sans tout reconstruire.
- Historique NON persisté entre sessions (pas de sauvegarde) — se vide à la fermeture du jeu,
  décision explicite (la plupart des chats de MMO ne survivent pas non plus à une relance).

## Portée exclue de cette v1 (explicitement, pas des oublis)

- Vrai réseau (Mirror) — World/Private/Nearby/AetherEcho restent en écho local jusqu'à ce que le
  réseau existe. Le point d'ancrage (`ChatSystem.TrySendPlayerMessage`) est commenté pour le
  signaler clairement au futur chantier réseau.
- Blocage permanent par canal façon Options Nostale (bloquer durablement le haut-parleur) — seul
  le filtre de vue temporaire existe. Ajoutable plus tard si demandé.
- Toast/bannière à l'écran en plus du log Système (AnyRPG fait ça en dual-path) — pas demandé,
  scope minimal : log seulement.
- Slash commands (`/w`, `/g`...) façon Nostale — le picker de canal suffit pour cette v1, pas de
  parsing de commande.
- Vraie cible de chuchotement (Private) — en solo, "Private" est juste un canal de plus en écho
  local, pas de sélection de destinataire réel (n'a de sens qu'avec le réseau).

## Tests (Florian, manuel Play Mode)

1. Taper dans World/Private/Alentour → le message apparaît immédiatement dans le flux, préfixé
   correctement.
2. Taper dans Guild → refusé, ligne "Pas de guilde." apparaît en Système.
3. Sans Écho d'Aether en inventaire → l'option n'apparaît pas dans le picker.
4. Avec 1 Écho d'Aether en inventaire → l'option apparaît, l'envoi consomme l'item (vérifier
   qu'il disparaît de l'inventaire) et poste une ligne stylée différemment.
5. Masquer le filtre World puis envoyer un Écho d'Aether → la ligne reste visible malgré le
   filtre (contrairement à une ligne World classique qui, elle, disparaît si on masque World).
6. Tuer un mob avec loot / terminer une quête / monter de niveau / recevoir un mail → une ligne
   Système apparaît automatiquement, sans action du joueur.
7. Changer de map (portail/donjon) puis retester 1-6 → tout doit continuer à fonctionner (vérifie
   que `Resubscribe()` est bien câblé, même bug que celui trouvé sur QuestTracker/QuestJournal
   cette session).
8. Utiliser un Écho d'Aether depuis la ConsoBar (ou double-clic inventaire) → ouvre le panel de
   saisie, ne consomme RIEN tant que non validé. Annuler → panel fermé, item toujours en
   inventaire. Valider avec un texte → item consommé, ligne Écho d'Aether postée — fonctionne
   même si `ChatUI` n'est pas à l'écran au moment de l'usage.
