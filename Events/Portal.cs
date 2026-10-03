using UnityEngine;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
    RequiresConditions   = 3, // ET de tous les critères renseignés : mobs, leviers, niveau, quêtes, réputation
                              // (anciennement RequiresTrigger — même ordinal 3, aucune donnée sérialisée à migrer)
}

[System.Serializable]
public class PortalQuestRequirement
{
    public QuestData quest;
    [Tooltip("État MINIMUM requis — comparé dans l'ordre None < Active < Completed < TurnedIn, donc " +
             "Completed accepte aussi une quête déjà rendue (TurnedIn). Completed = objectifs " +
             "terminés ; TurnedIn = quête rendue au PNJ ; Active = simplement en cours.")]
    public QuestState requiredState = QuestState.Completed;
}

[System.Serializable]
public class PortalActivatableRequirement
{
    [Tooltip("Glisse l'instance Activatable exacte depuis la Hierarchy (même scène).")]
    public Activatable activatable;
    [Tooltip("true = ce levier doit être ACTUELLEMENT On. false = doit être ACTUELLEMENT Off — " +
             "un levier actionné peut donc bloquer ce portail plutôt que le débloquer.")]
    public bool requiredState = true;
}

/// <summary>Une ligne d'objectif prête à afficher — produite par Portal.CollectObjectives(),
/// consommée par DungeonGroupPanelUI. showCount = false : afficher seulement fait/pas fait.</summary>
public class PortalObjective
{
    public string label;
    public int    current;
    public int    total;
    public bool   done;
    public bool   showCount;
}

public class Portal : MonoBehaviour
{
    [Header("Identité")]
    [Tooltip("ID unique de CE portail")]
    public string portalID = "Portal_A";

    [Header("Destination")]
#if UNITY_EDITOR
    [Tooltip("Glisse la scène ici — targetMap se remplit automatiquement avec son nom (voir " +
             "OnValidate()). Évite le bug vécu : un targetMap tapé/copié-collé à la main qui ne " +
             "correspond plus à la vraie destination du portail.")]
    public SceneAsset targetMapScene;
#endif
    // Caché de l'Inspector — targetMapScene le pilote entièrement (OnValidate). Reste un champ
    // public normal : c'est lui que TeleportRoutine()/SceneLoader lisent réellement au runtime.
    [HideInInspector]
    public string targetMap = "Map_02";

    [Tooltip("ID du portail dans la scène cible — le joueur spawne dessus")]
    public string targetPortalID = "Portal_B";

    [Header("Paramètres")]
    [Tooltip("Cooldown après spawn pour ne pas re-traverser immédiatement (secondes)")]
    public float spawnCooldown = 3f;

    [Tooltip("Délai avant téléportation (secondes)")]
    public float teleportDelay = 0.5f;

    [Header("Verrou")]
    [Tooltip("None = libre (comportement actuel, inchangé). Les 3 autres valeurs bloquent le " +
             "franchissement tant que leur condition n'est pas remplie.")]
    public PortalGateType gateType = PortalGateType.None;

    [Tooltip("Glisse le DungeonData attendu ici — linkedInstanceID (dungeonID) se remplit tout " +
             "seul (OnValidate). Évite de taper un string à la main qui peut diverger du vrai ID. " +
             "Visibilité gérée par Editor/PortalEditor.cs (pas [ShowIf] — même limitation Unity " +
             "sur les List<T> que DungeonMapData, tout l'Inspector est dessiné à la main ici).")]
    public DungeonData linkedDungeon;

    // Rempli automatiquement depuis linkedDungeon.dungeonID (OnValidate) — reste le champ
    // réellement comparé à InstanceSession.PendingInstance.InstanceID dans CanCross().
    [HideInInspector]
    public string linkedInstanceID = "";

    [Tooltip("Palier requis débloqué (Player.HasUnlockedTier).")]
    public int targetTier = 2;

    [Tooltip("Mobs à tuer — glisse les instances EXACTES depuis la Hierarchy (même scène). TOUS " +
             "doivent être morts (Entity.isDead). Laisser vide si ce portail ne dépend que de " +
             "leviers (requiredActivatables ci-dessous), pas de mob à tuer.")]
    public List<Mob> requiredMobs = new List<Mob>();

    [Tooltip("Levier(s) requis (Activatable) — état COURANT vérifié, pas cumulatif : un " +
             "levier remis Off reverrouille ce portail s'il exigeait On, et inversement. TOUS " +
             "les leviers listés doivent matcher leur état requis (puzzle multi-levier). Combiné " +
             "en ET avec requiredMobs au-dessus si les deux sont renseignés.")]
    public List<PortalActivatableRequirement> requiredActivatables = new List<PortalActivatableRequirement>();

    [Tooltip("Coché = le panel de donjon affiche « Mécanismes actionnés x / y » (x = leviers déjà " +
             "dans l'état On requis, y = total à activer). Décoché (défaut) = une seule ligne " +
             "« Résoudre le mécanisme » sans compteur, qui se coche quand TOUS les leviers sont " +
             "dans leur état requis. À laisser décoché pour un puzzle : un compteur qui bouge à " +
             "chaque essai révèle le bon levier, même en ne comptant que ceux à activer.")]
    public bool showActivatableCount = false;

    [Tooltip("Niveau minimum du joueur (Player.level). 0 = aucune exigence de niveau.")]
    public int minPlayerLevel = 0;

    [Tooltip("Quête(s) requise(s) — TOUTES doivent avoir atteint leur état minimum. Vide = aucune " +
             "exigence de quête.")]
    public List<PortalQuestRequirement> requiredQuests = new List<PortalQuestRequirement>();

    [Tooltip("Rang de Prestige minimum (Player.prestigeRank, voir spec Prestige/Aura §1.2). " +
             "0 = aucune exigence.")]
    public int minPrestigeRank = 0;

    [Header("Point d'atterrissage seul (sans retour)")]
    [Tooltip("Ce portail sert UNIQUEMENT de point de spawn (Start()/RepositionPlayer au " +
             "chargement de la scène) — ex: réception d'un donjon sans retour possible. " +
             "Jamais affiché (lockedVisual/openVisual ignorés) et jamais interactif " +
             "(OnTriggerEnter ne fait rien) : le joueur peut marcher dessus sans effet.")]
    public bool isHidden = false;

    [Header("Verrou — visuel (optionnel, 3 skins fixes — auto-sélectionnés par gateType)")]
    [Tooltip("Affiché quand CanCross() est actuellement faux (RequiresTierUnlock/RequiresConditions " +
             "uniquement). Sans effet sur les autres gateType.")]
    public GameObject lockedVisual;

    [Tooltip("Affiché quand CanCross() est actuellement vrai (RequiresTierUnlock/RequiresConditions), " +
             "OU en permanence pour un portail libre (gateType = None). Sans effet sur " +
             "RequiresDungeonEntry — voir dungeonVisual ci-dessous.")]
    public GameObject openVisual;

    [Tooltip("Affiché en permanence pour RequiresDungeonEntry — remplace openVisual pour ce " +
             "gateType précis (avant, openVisual servait aux deux sens à la fois, source de " +
             "confusion). Sans effet sur les autres gateType.")]
    public GameObject dungeonVisual;

    private static float _cooldownTimer = 0f;
    private bool _teleporting     = false;
    private bool _confirmPending  = false; // popup "Entrer dans le donjon" déjà ouverte, RequiresDungeonEntry uniquement
    private Player _localPlayer; // résolu paresseusement, réutilisé pour le check visuel par frame
    private bool   _wasOpen;     // état CanCross() de la frame précédente — édge-detection pour AnnoncePanel

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (linkedDungeon  != null) linkedInstanceID = linkedDungeon.dungeonID;
        if (targetMapScene != null) targetMap         = targetMapScene.name;
    }
#endif

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

        RefreshLockVisual();
    }

    /// <summary>Sélectionne AUTOMATIQUEMENT le bon skin selon gateType — poll par frame comme le
    /// reste des panels UI du projet (ConsoBarUI/PlayerInfosPanel), pas d'event dédié pour ce
    /// premier passage. Les 3 champs (lockedVisual/openVisual/dungeonVisual) sont fixes sur le
    /// Placeholder, jamais réassignés par instance — seul CE gateType décide lequel s'affiche :
    ///   - None               → openVisual en permanence, les 2 autres masqués.
    ///   - RequiresDungeonEntry → dungeonVisual en permanence, les 2 autres masqués.
    ///   - TierUnlock/Trigger → lockedVisual/openVisual basculent selon CanCross(), dungeonVisual
    ///     masqué.</summary>
    private void RefreshLockVisual()
    {
        if (isHidden)
        {
            SetVisuals(locked: false, open: false, dungeon: false);
            // Couvre aussi le mesh propre du portail (Prefab_Portal_Lock/Open ont leur visuel
            // directement dessus, pas seulement via les 3 skins enfants) — sans ça, isHidden ne
            // cachait rien sur ces prefabs-là, seulement sur un skin enfant assigné.
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = false;
            return;
        }
        // Si isHidden vient d'être décoché (ou n'a jamais été vrai), s'assure que les renderers
        // du portail lui-même sont bien actifs — sinon un portail resterait invisible pour
        // toujours après un seul frame où isHidden était vrai.
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = true;

        switch (gateType)
        {
            case PortalGateType.None:
                SetVisuals(locked: false, open: true, dungeon: false);
                return;

            case PortalGateType.RequiresDungeonEntry:
                SetVisuals(locked: false, open: false, dungeon: true);
                return;

            default: // RequiresTierUnlock / RequiresConditions
                if (_localPlayer == null) _localPlayer = FindObjectOfType<Player>();
                bool open = CanCross(_localPlayer);

                // Annonce de groupe — transition fermé→ouvert pendant un run actif uniquement
                // (voir Florian, spec annonces 2026-09-29 : "Portail ouvert", scope donjon
                // uniquement — un portail RequiresTierUnlock du monde ouvert ne doit jamais
                // spammer ce message, d'où la double garde gateType + CurrentInstance != null).
                if (gateType == PortalGateType.RequiresConditions && open && !_wasOpen
                    && InstanceSession.Exists && InstanceSession.Instance.CurrentInstance != null)
                    AnnoncePanel.Instance?.Announce("Le portail est ouvert");
                _wasOpen = open;

                SetVisuals(locked: !open, open: open, dungeon: false);
                return;
        }
    }

    private void SetVisuals(bool locked, bool open, bool dungeon)
    {
        if (lockedVisual  != null) lockedVisual.SetActive(locked);
        if (openVisual    != null) openVisual.SetActive(open);
        if (dungeonVisual != null) dungeonVisual.SetActive(dungeon);
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
            case PortalGateType.RequiresConditions:
                return UnmetCondition(player, describe: false) == null;
            default:
                return true;
        }
    }

    /// <summary>RequiresConditions combine en ET tous les critères renseignés, chacun optionnel :
    /// requiredMobs morts, requiredActivatables dans leur état, minPlayerLevel, requiredQuests,
    /// minPrestigeRank. Lit directement les instances référencées (même scène), aucun passage
    /// par InstanceSession. Retourne null si TOUT est rempli, sinon la 1re condition manquante —
    /// describe = false renvoie juste "" (pas d'allocation de string, appelé chaque frame par
    /// RefreshLockVisual) ; describe = true renvoie le texte affiché au joueur.</summary>
    private string UnmetCondition(Player player, bool describe)
    {
        foreach (var mob in requiredMobs)
        {
            if (mob == null) continue;
            if (!mob.isDead) return describe ? "Des ennemis gardent encore le passage" : "";
        }

        foreach (var req in requiredActivatables)
        {
            if (req == null || req.activatable == null) continue;
            if (req.activatable.IsOn != req.requiredState)
                return describe ? "Un mécanisme doit être actionné" : "";
        }

        if (minPlayerLevel > 0 && (player == null || player.level < minPlayerLevel))
            return describe ? $"Niveau {minPlayerLevel} requis" : "";

        foreach (var req in requiredQuests)
        {
            if (req == null || req.quest == null) continue;
            QuestState state = QuestSystem.Instance != null
                ? QuestSystem.Instance.GetQuestState(req.quest)
                : QuestState.None;
            if (state < req.requiredState)
                return describe ? $"Quête requise : {req.quest.questName}" : "";
        }

        if (minPrestigeRank > 0 && (player == null || player.prestigeRank < minPrestigeRank))
            return describe ? "Prestige insuffisant" : "";

        return null;
    }

    /// <summary>Alimente le panel de donjon avec ce que ce portail exige encore. Les mobs sont
    /// versés dans un ensemble partagé (dédoublonné entre portails, le panel les additionne en
    /// « Ennemis vaincus x / y » sans noms) ; leviers, niveau, quêtes et réputation deviennent
    /// des lignes. Ignore les portails qui ne sont pas RequiresConditions ou sont cachés.
    ///
    /// Leviers : JAMAIS de compteur par défaut (showActivatableCount = false) — voir son tooltip.
    /// Le total ne compte que les leviers à mettre On ; ceux qui doivent rester Off n'apparaissent
    /// nulle part, ils ne sont qu'une contrainte du puzzle.</summary>
    public void CollectObjectives(Player player, HashSet<Mob> mobs, List<PortalObjective> lines)
    {
        if (gateType != PortalGateType.RequiresConditions || isHidden) return;

        foreach (var mob in requiredMobs)
            if (mob != null) mobs.Add(mob);

        bool hasLever = false, allLeversOk = true;
        int leversToActivate = 0, leversActive = 0;
        foreach (var req in requiredActivatables)
        {
            if (req == null || req.activatable == null) continue;
            hasLever = true;
            bool on = req.activatable.IsOn;
            if (on != req.requiredState) allLeversOk = false;
            if (req.requiredState)
            {
                leversToActivate++;
                if (on) leversActive++;
            }
        }
        if (hasLever)
        {
            lines.Add(new PortalObjective
            {
                label     = showActivatableCount ? "Mécanismes actionnés" : "Résoudre le mécanisme",
                current   = leversActive,
                total     = leversToActivate,
                done      = allLeversOk,
                showCount = showActivatableCount && leversToActivate > 0,
            });
        }

        if (minPlayerLevel > 0)
        {
            lines.Add(new PortalObjective
            {
                label = $"Niveau {minPlayerLevel}",
                done  = player != null && player.level >= minPlayerLevel,
            });
        }

        foreach (var req in requiredQuests)
        {
            if (req == null || req.quest == null) continue;
            QuestState state = QuestSystem.Instance != null
                ? QuestSystem.Instance.GetQuestState(req.quest)
                : QuestState.None;
            lines.Add(new PortalObjective
            {
                label = $"Quête : {req.quest.questName}",
                done  = state >= req.requiredState,
            });
        }

        if (minPrestigeRank > 0)
        {
            lines.Add(new PortalObjective
            {
                label = "Prestige",
                done  = player != null && player.prestigeRank >= minPrestigeRank,
            });
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isHidden) return; // Point d'atterrissage passif — aucune interaction.

        Player player = other.GetComponent<Player>();
        if (player == null) return;
        if (_cooldownTimer > 0f) return;
        if (_teleporting) return;

        if (!CanCross(player))
        {
            Debug.Log($"[PORTAL] {portalID} — franchissement bloqué (gateType = {gateType}).");

            if (gateType == PortalGateType.RequiresConditions)
            {
                string reason = UnmetCondition(player, describe: true);
                if (!string.IsNullOrEmpty(reason))
                    FloatingText.Spawn(reason, player.transform.position + Vector3.up * 2f, new Color(1f, 0.45f, 0.35f));
            }
            return;
        }

        if (gateType == PortalGateType.RequiresDungeonEntry)
        {
            // L'entrée en donjon ne suit pas le chemin normal targetMap/targetPortalID — la
            // vraie destination vient de la config résolue par InstanceSession, pas de ce champ.
            if (_confirmPending) return;

            if (ConfirmationUI.Instance == null)
            {
                // Pas de popup en scène — comportement d'avant, entrée immédiate plutôt que
                // bloquer silencieusement pour toujours (_confirmPending ne serait jamais reset,
                // aucun callback ne viendrait jamais le faire).
                EnterDungeonNow();
                return;
            }

            _confirmPending = true;
            string dungeonName = InstanceSession.Instance?.PendingInstance?.DisplayName ?? "le donjon";
            ConfirmationUI.Instance.OpenConfirmFlow("Entrer dans le donjon", dungeonName, _ =>
            {
                _confirmPending = false;
                EnterDungeonNow();
            });
            return;
        }

        StartCoroutine(TeleportRoutine());
    }

    private void EnterDungeonNow()
    {
        bool entered = InstanceSession.Instance != null && InstanceSession.Instance.ConsumePendingEntry();
        if (!entered)
            Debug.LogWarning($"[PORTAL] {portalID} — ConsumePendingEntry() a échoué malgré CanCross() vrai.");
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponent<Player>() == null) return;
        _teleporting    = false;
        // Repart de zéro si le joueur annule la popup et s'éloigne — sinon _confirmPending
        // resterait bloqué à true pour toujours (ConfirmationUI n'a pas de callback
        // "annulé", voir Cancel côté ce composant).
        _confirmPending = false;
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
