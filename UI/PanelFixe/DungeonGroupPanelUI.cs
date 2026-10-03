using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Text;

// =============================================================
// DUNGEONGROUPPANELUI.CS — Panel groupe donjon (raid-style)
// Path : Assets/Scripts/UI/PanelFixe/DungeonGroupPanelUI.cs
// AetherTree GDD v3.6 — voir docs/superpowers/specs/2026-09-23-
// donjon-entry-flow-design.md §5.4
//
// Visible dès qu'une entrée est en attente (InstanceSession.PendingInstance)
// OU qu'une instance est active (CurrentInstance) — caché sinon. Comme
// PlayerInfosPanel, tout est relu en Update(), pas d'event dédié pour ce
// premier passage. En solo, Participants ne contient jamais que le joueur
// local — la structure (une ligne par participant) reste prête pour un
// futur groupe réel sans réécriture.
//
// SETUP HIERARCHY (exemple) :
//   DungeonGroupPanel (script ici — TOUJOURS actif, ne jamais désactiver)
//     └── PanelRoot            (← panelRoot, SetActive piloté par ce script)
//           ├── HeaderText
//           ├── ParticipantRowsParent
//           ├── ObjectivesText   (un seul TextMeshProUGUI multiligne, lignes générées en code)
//           └── FooterText       (vies COMMUNES du donjon, ex: "Vies du donjon ♥♥♥♡♡")
//
// Vies : deux niveaux, voir InstanceSession.OnPlayerDeath(). Chaque ligne participant affiche
// les vies du joueur (à côté du nom) ; footerText affiche les vies communes du donjon
// (IInstanceConfig.DeathLimit), rien si l'activité n'en a pas.
//
// Objectifs : recalculés toutes les objectivesRefreshInterval secondes depuis les Portal
// RequiresConditions de la scène (Portal.CollectObjectives) + le boss (Mob.isDungeonBoss).
// Mobs additionnés en une ligne "Ennemis vaincus x / y" sans noms ; leviers en une ligne binaire
// sans compteur par défaut (un compteur révélerait le bon levier d'un puzzle).
//
// IMPORTANT : panelRoot doit être un ENFANT distinct du GameObject qui porte ce
// script, JAMAIS le même GameObject. Si panelRoot == gameObject, le premier
// Update() sans instance active/en attente appelle panelRoot.SetActive(false)
// sur SOI-MÊME — Unity arrête alors d'appeler Update() sur ce composant pour de
// bon (aucun code du projet ne le réactive), le panel est brické de façon
// permanente. Voir garde dans Awake().
// =============================================================

public class DungeonGroupPanelUI : MonoBehaviour
{
    public static DungeonGroupPanelUI Instance { get; private set; }

    [Header("Racine")]
    [Tooltip("GameObject affiché/caché selon qu'une entrée est en attente ou active. DOIT être " +
             "un enfant distinct du GameObject portant ce script — jamais le même GameObject " +
             "(voir garde en Awake()).")]
    public GameObject panelRoot;

    [Header("Header")]
    public TextMeshProUGUI headerText;

    [Header("Corps")]
    [Tooltip("Préfab d'une ligne participant (nom + HP/MP) — instancié dynamiquement.")]
    public GameObject participantRowPrefab;
    [Tooltip("Parent où les lignes participant sont instanciées/détruites.")]
    public Transform participantRowsParent;

    [Header("Objectifs")]
    [Tooltip("Un seul texte multiligne (rich text) — titre + une ligne par objectif, généré en " +
             "code. Optionnel : laisser vide masque simplement la section.")]
    public TextMeshProUGUI objectivesText;
    [Tooltip("Secondes entre deux recalculs des objectifs (scan des Portal/Mob de la scène).")]
    public float objectivesRefreshInterval = 0.5f;
    [Tooltip("Message affiché à la place des objectifs tant que le portail du donjon n'est pas franchi.")]
    public string pendingHint = "Franchis le portail du donjon pour commencer.";

    [Header("Footer")]
    [Tooltip("Vies COMMUNES du donjon (DeathLimit) — rien d'affiché si l'activité n'en a pas.")]
    public TextMeshProUGUI footerText;

    [Header("Vies — apparence")]
    [Tooltip("Caractère d'une vie. Si votre police TMP n'a pas ♥ (carré vide/□ affiché), mettre " +
             "un caractère qu'elle possède, ex: ● ou *.")]
    public string lifeGlyph = "♥";
    public string lifeColorHex     = "#E0455F";
    public string lostLifeColorHex = "#363C4A";

    [Header("Objectifs — apparence")]
    [Tooltip("Marqueurs case cochée / à cocher. Mêmes précautions de police que lifeGlyph.")]
    public string doneGlyph = "[x]";
    public string todoGlyph = "[ ]";
    public string doneColorHex  = "#4CDB57";
    public string mutedColorHex = "#8D93A4";

    [Header("Actions")]
    [Tooltip("Quitte le groupe/run — comportement selon l'état (voir OnLeaveGroupClicked) : " +
             "AVANT le franchissement du portail gaté (PendingInstance), annule directement sans " +
             "confirmation (rien n'est en jeu). PENDANT un run actif (CurrentInstance), demande " +
             "confirmation via ConfirmationUI puis appelle InstanceSession.LeaveDungeon() — " +
             "expulsion volontaire immédiate, pas une mort (pas de vie perdue). En solo, " +
             "Participants ne contient jamais que le joueur = toujours chef " +
             "(InstanceSession.Leader côté ArmEntry) : \"quitter\" DISSOUT donc systématiquement " +
             "le groupe entier, pas de cas membre non-chef.")]
    public Button leaveGroupButton;

    private List<GameObject> _rows = new List<GameObject>();
    private float  _nextObjectivesRefresh;
    private Player _localPlayer;

    // Réutilisé à chaque refresh — copie triée de session.Participants (niveau desc), pas
    // d'allocation par frame. Voir RefreshParticipants().
    private readonly List<Player> _sortedParticipants = new List<Player>();

    // Réutilisés à chaque refresh d'objectifs — évite l'allocation de collections toutes les 0.5s.
    private readonly HashSet<Mob>          _objMobs  = new HashSet<Mob>();
    private readonly List<PortalObjective> _objLines = new List<PortalObjective>();
    private readonly List<PortalObjective> _objBoss  = new List<PortalObjective>();
    private readonly List<PortalObjective> _objAll   = new List<PortalObjective>();
    private readonly StringBuilder         _sb       = new StringBuilder();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (panelRoot == gameObject)
        {
            Debug.LogError("[DungeonGroupPanelUI] panelRoot est configuré sur le MÊME GameObject " +
                "que ce script — panelRoot.SetActive(false) désactiverait ce composant lui-même, " +
                "ce qui arrête Update() DÉFINITIVEMENT (rien ne le réactive ailleurs dans le " +
                "projet). panelRoot doit être un enfant distinct que ce script toggle. Voir le " +
                "commentaire SETUP HIERARCHY en tête de fichier. panelRoot mis à null pour éviter " +
                "l'auto-désactivation silencieuse.");
            panelRoot = null;
        }

        leaveGroupButton?.onClick.AddListener(OnLeaveGroupClicked);
    }

    private void OnLeaveGroupClicked()
    {
        var session = InstanceSession.Instance;
        if (session == null) return;

        // Confirmation dans TOUS les cas (Florian, 2026-09-29) — y compris "hors donjon"
        // (PendingInstance, portail pas encore franchi) : un clic accidentel sur ce bouton ne
        // doit jamais annuler quoi que ce soit sans prévenir, même quand rien n'est encore
        // matériellement perdu à annuler. ConfirmationUI (ex-TransactionConfirmUI, renommé
        // 2026-09-29 — sert maintenant à toute confirmation du jeu, pas seulement boutique).
        if (session.CurrentInstance != null)
        {
            ConfirmationUI.Instance?.OpenConfirmFlow(
                "Quitter le donjon",
                "Tu vas être expulsé du donjon sans récompense. Confirmer ?",
                _ => InstanceSession.Instance?.LeaveDungeon());
        }
        else if (session.PendingInstance != null)
        {
            ConfirmationUI.Instance?.OpenConfirmFlow(
                "Quitter le groupe",
                "Annuler l'entrée en attente pour ce donjon ?",
                _ => InstanceSession.Instance?.CancelPendingEntry());
        }
    }

    private void Update()
    {
        var session = InstanceSession.Instance;
        IInstanceConfig active = session != null
            ? session.CurrentInstance ?? session.PendingInstance
            : null;

        if (active == null)
        {
            if (panelRoot != null) panelRoot.SetActive(false);
            return;
        }

        if (panelRoot != null) panelRoot.SetActive(true);

        if (headerText != null) headerText.text = active.DisplayName;

        // Visible en permanence tant que le panel l'est — pending ou run actif, voir
        // OnLeaveGroupClicked() pour le comportement (annulation directe vs confirmation +
        // expulsion volontaire).
        if (leaveGroupButton != null)
            leaveGroupButton.gameObject.SetActive(true);

        RefreshParticipants(session, active);

        if (footerText != null)
        {
            int teamLives = session.TeamLivesRemaining;
            footerText.text = teamLives < 0
                ? ""
                : $"Vies du donjon  {Hearts(teamLives, active.DeathLimit)}";
        }

        RefreshObjectives(session);
    }

    // Vies restantes en couleur, vies perdues grisées — même nombre de glyphes en tout, pour que
    // la largeur ne bouge pas à chaque mort.
    private string Hearts(int remaining, int max)
    {
        if (max <= 0) return "";
        remaining = Mathf.Clamp(remaining, 0, max);

        _sb.Clear();
        _sb.Append("<color=").Append(lifeColorHex).Append('>');
        for (int i = 0; i < remaining; i++) _sb.Append(lifeGlyph);
        _sb.Append("</color><color=").Append(lostLifeColorHex).Append('>');
        for (int i = remaining; i < max; i++) _sb.Append(lifeGlyph);
        _sb.Append("</color>");
        return _sb.ToString();
    }

    // Objectifs de la scène courante, recalculés à intervalle fixe (scan des Portal/Mob, pas
    // gratuit chaque frame). Avant le franchissement du portail gaté (entrée en attente), montre
    // seulement pendingHint — il n'y a pas encore de scène de donjon à lire.
    private void RefreshObjectives(InstanceSession session)
    {
        if (objectivesText == null) return;

        if (session.CurrentInstance == null)
        {
            objectivesText.text = pendingHint;
            return;
        }

        if (Time.unscaledTime < _nextObjectivesRefresh) return;
        _nextObjectivesRefresh = Time.unscaledTime + objectivesRefreshInterval;

        if (_localPlayer == null) _localPlayer = FindObjectOfType<Player>();

        _objMobs.Clear();
        _objLines.Clear();
        _objBoss.Clear();
        _objAll.Clear();

        foreach (var portal in FindObjectsOfType<Portal>())
            portal.CollectObjectives(_localPlayer, _objMobs, _objLines);

        // Le boss a sa propre ligne (avec son nom) — retiré du compte d'ennemis s'il y figure aussi.
        foreach (var mob in FindObjectsOfType<Mob>())
        {
            if (!mob.isDungeonBoss) continue;
            _objMobs.Remove(mob);
            _objBoss.Add(new PortalObjective { label = $"Vaincre {mob.entityName}", done = mob.isDead });
        }

        if (_objMobs.Count > 0)
        {
            int killed = 0;
            foreach (var mob in _objMobs) if (mob.isDead) killed++;
            _objAll.Add(new PortalObjective
            {
                label = "Ennemis vaincus", current = killed, total = _objMobs.Count,
                done = killed >= _objMobs.Count, showCount = true,
            });
        }
        _objAll.AddRange(_objBoss);
        _objAll.AddRange(_objLines);

        if (_objAll.Count == 0) { objectivesText.text = ""; return; }

        int doneCount = 0;
        foreach (var line in _objAll) if (line.done) doneCount++;

        _sb.Clear();
        _sb.Append("<b>Objectifs</b>  <color=").Append(mutedColorHex).Append('>')
           .Append(doneCount).Append(" / ").Append(_objAll.Count).Append("</color>");

        foreach (var line in _objAll)
        {
            _sb.Append('\n');
            string count = line.showCount ? $"  {line.current} / {line.total}" : "";
            if (line.done)
            {
                _sb.Append("<color=").Append(doneColorHex).Append('>').Append(doneGlyph).Append("</color> ")
                   .Append("<color=").Append(mutedColorHex).Append("><s>").Append(line.label).Append("</s></color>")
                   .Append("<color=").Append(doneColorHex).Append('>').Append(count).Append("</color>");
            }
            else
            {
                _sb.Append(todoGlyph).Append(' ').Append(line.label).Append(count);
            }
        }
        objectivesText.text = _sb.ToString();
    }

    // Réconcilie _rows avec session.Participants (triés) au lieu de tout détruire/reconstruire
    // chaque frame (pattern établi par PlayerInfosPanel.RefreshEffects) — évite le churn GC
    // permanent pendant toute la durée d'un run, et le risque de MissingReferenceException si
    // participantRowPrefab est un template vivant sous participantRowsParent dans la scène.
    //
    // Trié par niveau décroissant (Florian, 2026-09-28) — le chef n'est PAS épinglé en tête, il
    // est juste coloré (voir InstanceSession.Leader, comparé PAR PARTICIPANT — pas un bool
    // global, sinon toute la liste se peindrait en jaune dès que le joueur local est chef).
    // HP/MP volontairement absents de la ligne (retiré du design — cette info existe déjà en
    // ciblant le joueur, voir clic ci-dessous) : seuls niveau/nom/vies du run restent.
    private void RefreshParticipants(InstanceSession session, IInstanceConfig active)
    {
        if (participantRowsParent == null || participantRowPrefab == null) return;

        _sortedParticipants.Clear();
        _sortedParticipants.AddRange(session.Participants);
        _sortedParticipants.Sort((a, b) => (b != null ? b.level : 0).CompareTo(a != null ? a.level : 0));

        // Instancier les lignes manquantes — le clic (cible le joueur lié, voir
        // DungeonParticipantRowUI) est câblé UNE FOIS ici, pas dans la boucle de mise à jour
        // ci-dessous (qui tourne chaque frame) : le lambda relit rowUI.BoundPlayer à l'instant du
        // clic, jamais figé sur le participant affiché au moment de l'instanciation — nécessaire
        // puisque le tri par niveau peut réassigner un participant différent à cette ligne d'une
        // frame à l'autre.
        while (_rows.Count < _sortedParticipants.Count)
        {
            var newRow = Instantiate(participantRowPrefab, participantRowsParent);
            _rows.Add(newRow);

            var rowUI = newRow.GetComponent<DungeonParticipantRowUI>();
            if (rowUI == null) rowUI = newRow.AddComponent<DungeonParticipantRowUI>();

            var btn = newRow.GetComponent<Button>();
            if (btn != null)
                btn.onClick.AddListener(() => TargetingSystem.Instance?.Select(rowUI.BoundPlayer));
        }

        // Détruire le surplus
        while (_rows.Count > _sortedParticipants.Count)
        {
            int last = _rows.Count - 1;
            Destroy(_rows[last]);
            _rows.RemoveAt(last);
        }

        // Mettre à jour chaque ligne en place
        for (int i = 0; i < _sortedParticipants.Count; i++)
        {
            var participant = _sortedParticipants[i];
            GameObject row = _rows[i];
            if (participant == null || row == null) continue;

            var rowUI = row.GetComponent<DungeonParticipantRowUI>();
            if (rowUI == null) rowUI = row.AddComponent<DungeonParticipantRowUI>();
            rowUI.BoundPlayer = participant;

            // 3 champs nommés (PlayerLevel/PlayerName/PlayerLives) — trouvés PAR NOM, pas par
            // ordre GetComponentsInChildren, pour rester robuste si la hiérarchie du prefab est
            // réordonnée plus tard.
            bool isLeader = participant == session.Leader;
            SetText(row.transform.Find("PlayerLevel")?.GetComponent<TextMeshProUGUI>(),
                $"Lv.{participant.level}", isLeader);
            SetText(row.transform.Find("PlayerName")?.GetComponent<TextMeshProUGUI>(),
                participant.entityName, isLeader);
            SetText(row.transform.Find("PlayerLives")?.GetComponent<TextMeshProUGUI>(),
                Hearts(session.GetPlayerLives(participant), active.LivesPerPlayer), null);
        }
    }

    private static void SetText(TextMeshProUGUI label, string value, bool? leaderColor)
    {
        if (label == null) return;
        label.text = value;
        if (leaderColor.HasValue) label.color = leaderColor.Value ? Color.yellow : Color.white;
    }
}

// =============================================================
// DUNGEONPARTICIPANTROWUI — posé automatiquement sur chaque ligne instanciée
// Porte juste une référence live au joueur affiché par CETTE ligne — le bouton de la ligne (si
// participantRowPrefab en a un) cible ce joueur au clic, voir RefreshParticipants().
// =============================================================
public class DungeonParticipantRowUI : MonoBehaviour
{
    [HideInInspector] public Player BoundPlayer;
}
