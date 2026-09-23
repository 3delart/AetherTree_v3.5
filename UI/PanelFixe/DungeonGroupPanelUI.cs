using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

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
//           └── FooterText
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

    [Header("Footer")]
    public TextMeshProUGUI footerText;

    private List<GameObject> _rows = new List<GameObject>();

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

        RefreshParticipants(session);

        if (footerText != null)
        {
            int lives = session.CurrentInstance != null ? session.LivesRemaining : active.LivesPerPlayer;
            footerText.text = $"Vies restantes : {lives}";
        }
    }

    // Réconcilie _rows avec session.Participants au lieu de tout détruire/reconstruire chaque
    // frame (pattern établi par PlayerInfosPanel.RefreshEffects) — évite le churn GC permanent
    // pendant toute la durée d'un run, et le risque de MissingReferenceException si
    // participantRowPrefab est un template vivant sous participantRowsParent dans la scène.
    private void RefreshParticipants(InstanceSession session)
    {
        if (participantRowsParent == null || participantRowPrefab == null) return;

        var participants = session.Participants;

        // Instancier les lignes manquantes
        while (_rows.Count < participants.Count)
            _rows.Add(Instantiate(participantRowPrefab, participantRowsParent));

        // Détruire le surplus
        while (_rows.Count > participants.Count)
        {
            int last = _rows.Count - 1;
            Destroy(_rows[last]);
            _rows.RemoveAt(last);
        }

        // Mettre à jour chaque ligne en place
        for (int i = 0; i < participants.Count; i++)
        {
            var participant = participants[i];
            GameObject row = _rows[i];
            if (participant == null || row == null) continue;

            TextMeshProUGUI[] texts = row.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length > 0)
            {
                texts[0].text = participant.entityName;
                texts[0].color = session.IsLeader ? Color.yellow : Color.white;
            }
            if (texts.Length > 1)
                texts[1].text = $"{participant.CurrentHP:0}/{participant.MaxHP:0} HP — " +
                                 $"{participant.CurrentMana:0}/{participant.MaxMana:0} MP";
        }
    }
}
