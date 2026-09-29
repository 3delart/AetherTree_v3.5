using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================
// WORLDBOSSDATA.CS — Configuration complète de l'événement Boss Géant
// Path : Assets/Scripts/Data/Content/WorldBossData.cs
// Spec : docs/superpowers/specs/2026-09-29-world-event-scheduler-boss-geant-design.md
//
// Un seul asset, glissé sur WorldEventScheduler.eventData — même patron que
// Player.prestigeAuraData (Data/Progression/PrestigeAuraData.cs) : toute la config d'un système
// dans UN asset partagé, éditable sans recompiler, plutôt que dispersée sur les champs Inspector
// d'un composant attaché à une scène précise (Florian, 2026-09-29 : "le world boss data devrait
// être les paramètres de tout l'événement").
//
// Le niveau du boss n'est PAS stocké ici : il est fixé au palier de la map tirée au moment de
// l'événement (mobLevel = palier, voir WorldEventScheduler), pas une propriété de l'asset.
// =============================================================

[System.Serializable]
public class WorldEventMapEntry
{
#if UNITY_EDITOR
    [Tooltip("Glisse la scène ici — sceneName se remplit automatiquement (voir " +
             "WorldBossData.OnValidate). Editor-only, n'existe pas en build : sceneName reste le " +
             "champ réellement lu au runtime, même convention que Portal.targetMapScene/" +
             "DungeonMapData.mapScene.")]
    public SceneAsset mapScene;
#endif
    [HideInInspector] public string sceneName;

    [Tooltip("Palier de CETTE scène — doit correspondre au MapInfo.palier posé dans la scène " +
             "elle-même. Pas de lecture automatique possible (une scène non chargée n'a pas de " +
             "MapInfo accessible) : à retaper ici à la main, une seule fois à la config.")]
    public int palier;
}

[CreateAssetMenu(fileName = "wboss_", menuName = "AetherTree/Contenu/WorldBossData")]
public class WorldBossData : ScriptableObject
{
    [Header("Boss")]
    [Tooltip("Un mob est tiré au hasard parmi ceux-ci à chaque événement Boss Géant. Devrait " +
             "avoir MobType = BossWorld (juste un avertissement si un autre type est glissé ici, " +
             "pas un blocage — même discipline que le reste du projet).")]
    public List<MobData> possibleBosses = new List<MobData>();

    [Header("Timer — intervalle aléatoire entre deux événements (secondes)")]
    [Tooltip("Défaut proche de SpawnManager (boss de map, 3600-7200s). Pour tester en Play Mode " +
             "sans attendre une heure, réduis CES DEUX VALEURS *ET* les deux offsets d'annonce " +
             "ci-dessous ENSEMBLE (ex: min/max=30/60, offsets=10/3) — les offsets sont un délai " +
             "AVANT le spawn, ils doivent rester plus petits que l'intervalle pour garder un sens.")]
    public float minInterval = 3600f;
    public float maxInterval = 7200f;

    [Header("Annonces — délai AVANT le spawn (secondes)")]
    public float firstWarningOffset  = 300f; // 5 min
    public float secondWarningOffset = 60f;  // 1 min

    [Header("Paliers éligibles")]
    public List<WorldEventMapEntry> eligibleMaps = new List<WorldEventMapEntry>();

    [Header("Résolution")]
    [Tooltip("Si le boss n'est pas tué dans ce délai après son spawn, il despawn.")]
    public float despawnTimeout = 1800f; // 30 min

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (possibleBosses != null)
            foreach (var boss in possibleBosses)
                if (boss != null && boss.mobType != MobType.BossWorld)
                    Debug.LogWarning($"[WorldBossData] {name} : {boss.mobName} a mobType = " +
                        $"{boss.mobType}, attendu BossWorld pour un Boss Géant.");

        if (eligibleMaps != null)
            foreach (var entry in eligibleMaps)
                if (entry.mapScene != null) entry.sceneName = entry.mapScene.name;
    }
#endif
}
