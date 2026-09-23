using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
//   DungeonGroupPanelUI (racine — SetActive piloté par ce script)
//     ├── HeaderText            (TMP — nom du donjon)
//     ├── ParticipantRowsParent (Transform — 1 ligne instanciée par participant)
//     └── FooterText            (TMP — vies restantes)
// =============================================================

public class DungeonGroupPanelUI : MonoBehaviour
{
    public static DungeonGroupPanelUI Instance { get; private set; }

    [Header("Racine")]
    [Tooltip("GameObject affiché/caché selon qu'une entrée est en attente ou active.")]
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

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
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

    private void RefreshParticipants(InstanceSession session)
    {
        if (participantRowsParent == null || participantRowPrefab == null) return;

        for (int i = participantRowsParent.childCount - 1; i >= 0; i--)
            Destroy(participantRowsParent.GetChild(i).gameObject);

        foreach (var participant in session.Participants)
        {
            if (participant == null) continue;

            GameObject row = Instantiate(participantRowPrefab, participantRowsParent);
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
