using UnityEngine;
using System.Collections.Generic;

// =============================================================
// COMBATVAGUEDATA.CS — ScriptableObject template d'événement Combat à Vague
// Path : Assets/Scripts/Data/Content/CombatVagueData.cs
// AetherTree GDD v3.6 — §14.5.1
//
// Type SO dédié — le GDD lui-même avait abandonné un "EventData SO"
// générique pour ce contenu (structure vagues/timers/tranches trop
// différente d'un donjon salles/couloir, voir DungeonData). Pas de
// contenu réel (waves reste vide) dans ce chantier — voir
// docs/superpowers/specs/2026-09-23-instance-system-design.md §3.
// =============================================================

public enum ExitPortalTrigger
{
    OnBossDeath = 0,
}

public enum BossType
{
    A = 0,
    B = 1,
}

[System.Serializable]
public class WaveMobEntry
{
    [Tooltip("Le mob à spawner.")]
    public MobData mobData;
    [Tooltip("Nombre d'exemplaires.")]
    public int count = 1;
    [Tooltip("Fourchette de niveau — calculée dynamiquement selon la tranche du joueur.")]
    public int levelMin = 1;
    public int levelMax = 1;
}

[System.Serializable]
public class CombatVagueWaveData
{
    [Tooltip("Index de la vague (1 à waveCount).")]
    public int waveIndex = 1;
    [Tooltip("Timestamp depuis le début, en secondes.")]
    public float spawnTimestamp = 0f;
    [Tooltip("Mobs à spawner pour cette vague.")]
    public List<WaveMobEntry> mobEntries = new List<WaveMobEntry>();
    [Tooltip("true uniquement sur la dernière vague.")]
    public bool hasBoss = false;
    [Tooltip("Boss de la dernière vague — null si hasBoss = false.")]
    public MobData bossData;
}

[CreateAssetMenu(fileName = "vague_", menuName = "AetherTree/Contenu/CombatVagueData")]
public class CombatVagueData : ScriptableObject, IInstanceConfig
{
    // ── Identité ──────────────────────────────────────────────
    [Header("Identité")]
    [Tooltip("Identifiant unique — snake_case.")]
    public string eventID = "";
    public string displayName = "";
    [Tooltip("Nom de la scène Unity de cet événement.")]
    public string sceneName = "";
    [Tooltip("dungeonID du ConsumableData requis pour entrer — même mécanisme que DungeonStone.")]
    public string entryItemID = "";

    // ── Affectation par tranche (multijoueur — sans effet en solo) ──
    [Header("Tranches (multijoueur — sans effet observable en solo)")]
    public int  maxPlayersPerInstance = 20;
    public bool instancePerTranche    = true;
    public int  trancheSize           = 10;

    // ── Structure ─────────────────────────────────────────────
    [Header("Structure")]
    public int   waveCount            = 5;
    public float preparationDuration  = 30f;
    public float interWaveDuration    = 30f;
    public BossType bossType          = BossType.A;
    public ExitPortalTrigger exitPortalAppears = ExitPortalTrigger.OnBossDeath;
    public float expulsionDelay       = 900f;
    public float timerMax             = 900f;
    public List<CombatVagueWaveData> waves = new List<CombatVagueWaveData>();

    // ── Récompenses ───────────────────────────────────────────
    [Header("Récompenses")]
    [Tooltip("Formule descriptive — voir GDD §14.5.1. Calcul réel implémenté au chantier contenu.")]
    public string rewardAerisFormula    = "1000 * niveau du joueur";
    public string rewardWorldRepFormula = "50 * tranche du joueur";

    // ── IInstanceConfig ───────────────────────────────────────
    public string InstanceID     => eventID;
    public string DisplayName    => displayName;
    public string SceneName      => sceneName;
    public string EntryItemID    => entryItemID;
    // Toujours 1 — GDD §14.5.1 : "aucune résurrection possible... chaque mort est définitive
    // pour ce run", jamais configurable, contrairement à DungeonData.livesPerPlayer.
    public int LivesPerPlayer => 1;
}
