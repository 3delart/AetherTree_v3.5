using UnityEngine;
using System.Collections.Generic;

// =============================================================
// DUNGEONDATA.CS — ScriptableObject template de donjon
// Path : Assets/Scripts/Data/Content/DungeonData.cs
// AetherTree GDD v3.6 — §14.2.3
//
// Couvre Donjon Classique ET Donjon de Déblocage via dungeonType — pas deux
// classes séparées (les deux partagent toute la structure GDD, seules les
// valeurs par défaut de livesPerPlayer/deathLimit/bossType diffèrent, voir
// §14.3.3 "Règles Spécifiques" du GDD). Pas de contenu réel de salle dans
// ce chantier (spawners/triggers/lockedUntil pas encore ajoutés à
// DungeonMapData) — voir docs/superpowers/specs/2026-09-23-instance-
// system-design.md §3 Non-objectifs.
// =============================================================

public enum DungeonType
{
    Classic    = 0,
    Unlock     = 1,
    FactionPvP = 2,
}

[System.Serializable]
public class DungeonMapData
{
    [Tooltip("Identifiant de la salle — doit correspondre au nom de la scène Unity chargée.")]
    public string mapID;
    [Tooltip("Nom affiché à l'entrée de la salle.")]
    public string displayName;
    [Tooltip("true = salle de boss.")]
    public bool   isBossRoom;
}

[CreateAssetMenu(fileName = "dgn_", menuName = "AetherTree/Contenu/DungeonData")]
public class DungeonData : ScriptableObject, IInstanceConfig
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    [Tooltip("Identifiant unique — snake_case, immuable.")]
    public string dungeonID = "";
    public string displayName = "";
    [Tooltip("Classic = Donjon Classique · Unlock = Donjon de Déblocage · FactionPvP = plus tard.")]
    public DungeonType dungeonType = DungeonType.Classic;
    public int tier = 1;
    public int levelMin = 1;
    public int levelMax = 10;

    // ── Accès ─────────────────────────────────────────────────
    [Header("Accès")]
    [Tooltip("Capacité max — Classique : 15 (voir GDD §14.2.3). Sans effet observable en solo.")]
    public int maxPlayers = 15;
    [Tooltip("itemID du ConsumableData (DungeonStone) requis pour entrer.")]
    public string entryItemID = "";

    // ── Structure ─────────────────────────────────────────────
    [Header("Structure")]
    [Tooltip("false = donjon direct boss (une seule salle).")]
    public bool hasCorridor = false;
    [Tooltip("Salles dans l'ordre — 1 seule si hasCorridor = false.")]
    public List<DungeonMapData> maps = new List<DungeonMapData>();
    [Tooltip("Boss dans l'ordre.")]
    public List<MobData> bossData = new List<MobData>();

    // ── Règles de mort — voir InstanceSession.OnPlayerDeath() ──
    [Header("Règles de mort")]
    [Tooltip("Vies individuelles par joueur. Classique : 2 (défaut). Déblocage : 1.")]
    public int livesPerPlayer = 2;
    [Tooltip("Morts collectives avant échec — 0 = illimité. Sans effet observable en solo (un " +
             "seul joueur, livesPerPlayer détermine déjà la fin de run avant que ce champ ne " +
             "soit jamais pertinent), champ gardé fidèle au GDD pour le futur multijoueur.")]
    public int deathLimit = 5;

    // ── Couloir ───────────────────────────────────────────────
    [Header("Couloir")]
    public bool  mobRespawn   = false;
    public float respawnDelay = 30f;

    // ── Récompenses ───────────────────────────────────────────
    [Header("Récompenses")]
    public ScriptableObject donjonLootTable;
    public int worldRepGain = 0;
    public ScriptableObject codexEntry;

    // ── IInstanceConfig ───────────────────────────────────────
    public string InstanceID     => dungeonID;
    public string DisplayName    => displayName;
    public string SceneName      => maps.Count > 0 ? maps[0].mapID : null;
    public string EntryItemID    => entryItemID;
    public int    LivesPerPlayer => livesPerPlayer;
}
