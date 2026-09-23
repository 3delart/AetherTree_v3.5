using UnityEngine;
using System.Collections;

// =============================================================
// PORTAL.CS — Portail bidirectionnel entre maps
// Path : Assets/Scripts/World/Portal.cs
// AetherTree GDD v30 — Section 17
//
// Chaque portail a un ID unique. Le portail de destination
// sert de point de spawn — le joueur apparaît dessus.
//
// Setup Unity (exemple Map_01 ↔ Map_02) :
//   Portail A (dans Map_01) :
//     - portalID       = "Portal_A"
//     - targetMap      = "Map_02"
//     - targetPortalID = "Portal_B"
//   Portail B (dans Map_02) :
//     - portalID       = "Portal_B"
//     - targetMap      = "Map_01"
//     - targetPortalID = "Portal_A"
//
//   Collider Trigger sur le GO.
// =============================================================
public enum PortalGateType
{
    None                 = 0, // Comportement actuel — libre, aucun changement pour l'existant
    RequiresDungeonEntry = 1, // InstanceSession.PendingInstance valide pour ce portail précis
    RequiresTierUnlock   = 2, // Player.HasUnlockedTier(targetTier)
    RequiresTrigger      = 3, // InstanceSession.IsTriggerMet(requiredTriggerID)
}

public class Portal : MonoBehaviour
{
    [Header("Identité")]
    [Tooltip("ID unique de CE portail")]
    public string portalID = "Portal_A";

    [Header("Destination")]
    [Tooltip("Scène cible (doit être dans Build Settings)")]
    public string targetMap = "Map_02";

    [Tooltip("ID du portail dans la scène cible — le joueur spawne dessus")]
    public string targetPortalID = "Portal_B";

    [Tooltip("Nom affiché au joueur")]
    public string destinationLabel = "";

    [Header("Paramètres")]
    [Tooltip("Cooldown après spawn pour ne pas re-traverser immédiatement (secondes)")]
    public float spawnCooldown = 3f;

    [Tooltip("Délai avant téléportation (secondes)")]
    public float teleportDelay = 0.5f;

    [Header("Verrou")]
    [Tooltip("None = libre (comportement actuel, inchangé). Les 3 autres valeurs bloquent le " +
             "franchissement tant que leur condition n'est pas remplie.")]
    public PortalGateType gateType = PortalGateType.None;

    [Tooltip("InstanceID du donjon attendu — doit matcher IInstanceConfig.InstanceID de la " +
             "config actuellement en attente (InstanceSession.PendingInstance).")]
    [ShowIf(nameof(gateType), PortalGateType.RequiresDungeonEntry)]
    public string linkedInstanceID = "";

    [Tooltip("Palier requis débloqué (Player.HasUnlockedTier).")]
    [ShowIf(nameof(gateType), PortalGateType.RequiresTierUnlock)]
    public int targetTier = 2;

    [Tooltip("triggerID requis acquis pendant le run en cours (InstanceSession.IsTriggerMet).")]
    [ShowIf(nameof(gateType), PortalGateType.RequiresTrigger)]
    public string requiredTriggerID = "";

    private static float _cooldownTimer = 0f;
    private bool _teleporting  = false;

    private void Start()
    {
        // Si le joueur arrive via ce portail, le repositionne ici
        if (PortalTransferData.TargetPortalID == portalID)
        {
            RepositionPlayer();
            _cooldownTimer = spawnCooldown;
            PortalTransferData.Clear();
        }
    }

    private void Update()
    {
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;
    }

    private bool CanCross(Player player)
    {
        switch (gateType)
        {
            case PortalGateType.None:
                return true;
            case PortalGateType.RequiresDungeonEntry:
                return InstanceSession.Instance != null
                    && InstanceSession.Instance.PendingInstance != null
                    && !string.IsNullOrEmpty(linkedInstanceID)
                    && InstanceSession.Instance.PendingInstance.InstanceID == linkedInstanceID;
            case PortalGateType.RequiresTierUnlock:
                return player != null && player.HasUnlockedTier(targetTier);
            case PortalGateType.RequiresTrigger:
                return InstanceSession.Instance != null
                    && InstanceSession.Instance.IsTriggerMet(requiredTriggerID);
            default:
                return true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        Player player = other.GetComponent<Player>();
        if (player == null) return;
        if (_cooldownTimer > 0f) return;
        if (_teleporting) return;

        if (!CanCross(player))
        {
            Debug.Log($"[PORTAL] {portalID} — franchissement bloqué (gateType = {gateType}).");
            return;
        }

        if (gateType == PortalGateType.RequiresDungeonEntry)
        {
            // L'entrée en donjon ne suit pas le chemin normal targetMap/targetPortalID — la
            // vraie destination vient de la config résolue par InstanceSession, pas de ce champ.
            bool entered = InstanceSession.Instance != null && InstanceSession.Instance.ConsumePendingEntry();
            if (!entered)
                Debug.LogWarning($"[PORTAL] {portalID} — ConsumePendingEntry() a échoué malgré CanCross() vrai.");
            return;
        }

        StartCoroutine(TeleportRoutine());
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponent<Player>() == null) return;
        _teleporting = false;
    }

    private IEnumerator TeleportRoutine()
    {
        _teleporting = true;
        Debug.Log($"[PORTAL] {portalID} → {targetMap} ({targetPortalID})");
        yield return new WaitForSeconds(teleportDelay);
        PortalTransferData.TargetPortalID = targetPortalID;
        SceneLoader.Instance?.LoadMap(targetMap);
    }

    private void RepositionPlayer()
    {
        var player = FindObjectOfType<Player>();
        if (player == null) return;
        Vector3 spawnPos = transform.position + transform.forward * 1.5f;
        var agent = player.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null) agent.Warp(spawnPos);
        else player.transform.position = spawnPos;
        player.transform.rotation = transform.rotation;
        Debug.Log($"[PORTAL] Joueur spawné sur {portalID}");
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.3f);
        Gizmos.DrawCube(transform.position, new Vector3(2f, 3f, 0.5f));
        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.9f);
        Gizmos.DrawWireCube(transform.position, new Vector3(2f, 3f, 0.5f));
        UnityEditor.Handles.color = Color.cyan;
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2f,
            $"{portalID} → {targetMap}/{targetPortalID}");
    }
#endif
}

// =============================================================
// PORTALTRANSFERDATA — données persistantes entre scènes
// =============================================================
public static class PortalTransferData
{
    public static string TargetPortalID { get; set; } = "";
    public static void Clear() => TargetPortalID = "";
}
