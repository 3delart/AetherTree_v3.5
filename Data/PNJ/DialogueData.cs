using UnityEngine;
using System.Collections.Generic;

// =============================================================
// DIALOGUEDATA — ScriptableObject d'une conversation PNJ
// Path : Assets/Scripts/Data/DialogueData.cs
// AetherTree GDD v30 — §19 (PNJ & Dialogues)
//
// Structure : liste de DialogueStage numérotés.
// Chaque stage peut avoir :
//   — des conditions d'accès (réputation, niveau, quête, item) — §19.1
//   — des options cliquables menant à d'autres stages
//   — des récompenses déclenchées à l'entrée du stage — §19.1
// Stage terminal (options vides) = fin de conversation.
//
// Créer via : Assets > Create > AetherTree > PNJ > DialogueData
// =============================================================

[CreateAssetMenu(fileName = "dlg_", menuName = "AetherTree/PNJ/DialogueData")]
public class DialogueData : ScriptableObject
{
    [Header("Dialogue")]
    [Tooltip("Clé technique STABLE — ne change jamais. Convention : snake_case, préfixe \"dlg_\"\n" +
             "(ex: \"dlg_forgeron_braven\"). Ne JAMAIS afficher au joueur — voir dialogueName pour l'affichage.")]
    public string             dialogueID;
    public string             dialogueName = "Dialogue";
    public List<DialogueStage> stages      = new List<DialogueStage>();

    /// <summary>Retourne le stage par son ID. Null si inexistant.</summary>
    public DialogueStage GetStage(int stageID)
    {
        foreach (DialogueStage s in stages)
            if (s.stageID == stageID) return s;
        return null;
    }

    /// <summary>Retourne le premier stage (stageID le plus bas).</summary>
    public DialogueStage GetFirstStage()
    {
        if (stages == null || stages.Count == 0) return null;
        DialogueStage first = stages[0];
        foreach (DialogueStage s in stages)
            if (s.stageID < first.stageID) first = s;
        return first;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(dialogueID))
            dialogueID = name;
    }
#endif
}

// ── Stage de dialogue ─────────────────────────────────────────
[System.Serializable]
public class DialogueStage
{
    [Tooltip("Identifiant unique du stage dans ce dialogue")]
    public int    stageID   = 0;

    [Tooltip("Si coché : ce stage est sauté automatiquement quand le joueur est déjà connu du PNJ.\nUtile pour le stage de présentation (stage 0).")]
    public bool   skipIfKnown = false;

    [Tooltip("Si coché : texte et boutons calculés à runtime selon l'état des quêtes du PNJ.\nUtilisé par les PNJ Quest — Accepter / Récupérer / Partir générés automatiquement.")]
    public bool   isDynamicQuestStage = false;

    [TextArea(2, 6)]
    [Tooltip("Texte affiché dans la bulle du PNJ (ignoré si isDynamicQuestStage est coché)")]
    public string text      = "";

    // ── Conditions d'accès — GDD v30 §19.1 ───────────────────
    [Tooltip("Rang de Réputation Monde minimum requis pour accéder à ce stage (0 = aucun)")]
    public int   requiredWorldReputationRank = 0;

    [Tooltip("Niveau joueur minimum requis (0 = aucun)")]
    public int   requiredLevel = 0;

    [Tooltip("ID de quête devant être en cours pour accéder à ce stage (vide = aucune)")]
    public string requiredQuestID = "";

    [Tooltip("ID d'item devant être dans l'inventaire (vide = aucun)")]
    public string requiredItemID  = "";

    // ── Récompenses — GDD v30 §19.1 ──────────────────────────
    // Déclenchées à l'entrée dans ce stage (une seule fois par visite).
    [Tooltip("XP accordée à l'entrée de ce stage (0 = aucune)")]
    public int   rewardXP          = 0;

    [Tooltip("Aeris accordés à l'entrée de ce stage (0 = aucun)")]
    public int   rewardAeris       = 0;

    [Tooltip("ID d'item ajouté à l'inventaire à l'entrée (vide = aucun)")]
    public string rewardItemID     = "";

    [Tooltip("Points de Réputation Monde accordés (peut être négatif)")]
    public int   rewardWorldRep    = 0;

    [Tooltip("Options cliquables proposées au joueur. Vide = fin de conversation.")]
    public List<DialogueOption> options = new List<DialogueOption>();
}

// ── Option de dialogue ────────────────────────────────────────
[System.Serializable]
public class DialogueOption
{
    [Tooltip("Texte du bouton affiché au joueur")]
    public string label        = "...";

    [Tooltip("Stage vers lequel cette option mène (-1 = ferme le dialogue)")]
    public int    nextStageID  = -1;

    [Tooltip("Action déclenchée en choisissant cette option")]
    public DialogueAction action = DialogueAction.None;

    [Tooltip("Quête concernée — utilisé pour AcceptQuest et TurnInQuest")]
    [ShowIf(nameof(action), DialogueAction.AcceptQuest, DialogueAction.TurnInQuest)]
    public QuestData questData = null;
}

// ── Actions possibles depuis une option — GDD v30 §19 ─────────
public enum DialogueAction
{
    None,               // Aucune action — juste navigation de stage
    OpenShop,           // Ouvre ShopUI (Merchant)
    OpenForge,          // Ouvre ForgeUI — onglet Upgrade actuel (PNJType.Forge) — TODO Phase 6
    OpenRarity,         // Ouvre RarityUI (Rarity) — pari de rareté GDD §3.4.8
    OpenRuneUI,         // Ouvre RuneUI identification/insertion (Antiquarian) — TODO Phase 6
    OpenFusionUI,       // Ouvre FusionUI Gants/Bottes (FusionNPC) — TODO Phase 6
    [System.Obsolete("Retiré du design (2026) — métier jamais implémenté, ordinal gardé.")]
    OpenMetierUI,
    OpenQuestLog,       // Ouvre QuestUI (Quest) — TODO Phase 7
    OpenHarborUI,       // Ouvre navigation bateau (HarborMaster) — TODO Phase 8
    TriggerGuildCreation, // Lance la création de guilde (Mayor)
    AcceptQuest,        // Accepte une quête (QuestNPC) — questData assignée sur le stage
    TurnInQuest,        // Rend une quête complétée au PNJ donneur
    CloseDialogue,      // Ferme le dialogue

    // ── Ajouté §13.2 — fenêtre PNJ partagée à onglets ──────────
    // Remplace OpenShop/OpenForge/OpenRarity pour tout PNJ composable (voir
    // PNJTypeExtensions.GetTabs()) — ouvre toujours sur l'onglet Boutique,
    // les autres onglets se changent depuis l'intérieur de la fenêtre, pas
    // via une nouvelle DialogueAction. Ajouté en fin d'enum — ne jamais
    // réordonner (int sérialisé sur les DialogueOption existantes).
    OpenPNJWindow,
}