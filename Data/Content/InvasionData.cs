using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

// =============================================================
// INVASIONDATA.CS — Configuration de l'événement Invasion (vagues + renforts)
// Path : Assets/Scripts/Data/Content/InvasionData.cs
// Spec : docs/superpowers/specs/2026-09-30-world-event-generic-invasion-design.md
//
// Hérite de WorldEventData — même tronc commun que WorldBossData (tirage de palier, tracking de
// participation, récompense finale). possibleVariants permet plusieurs thèmes (ex: élémentaires)
// avec chacun ses propres vagues/renforts/boss/récompense — jamais partagés entre variantes.
//
// Déroulement : point d'ancrage aléatoire sur le NavMesh du palier tiré, 5 vagues fixes qui
// popent sur un timer SANS attendre que la précédente soit clear (les mobs s'accumulent), le
// boss spawn une seule fois après la dernière vague, puis des renforts repopent en boucle sur le
// même timer tant que le boss reste vivant. Boss mort → plus aucun spawn → l'événement n'est
// résolu (et ne récompense) qu'une fois TOUS les mobs d'invasion restants également morts.
// =============================================================

[System.Serializable]
public class InvasionMobEntry
{
    public MobData mob;
    public int     count = 1;
}

[System.Serializable]
public class InvasionWave
{
    public List<InvasionMobEntry> mobs = new List<InvasionMobEntry>();
}

[System.Serializable]
public class InvasionVariant
{
    [Tooltip("Nom d'affichage — ex: Feu, Eau, Corruption... Utilisé dans les annonces.")]
    public string variantName = "Invasion";

    [Tooltip("Exactement 5 vagues attendues (pas de blocage dur si différent).")]
    public List<InvasionWave> waves = new List<InvasionWave>();

    [Tooltip("Repop en boucle sur le même timer tant que le boss reste vivant après son apparition.")]
    public List<InvasionMobEntry> reinforcements = new List<InvasionMobEntry>();

    [Tooltip("Devrait avoir MobType = EventInvasionMob + InvasionRole = Boss.")]
    public MobData   boss;
    public LootTable rewardTable;
}

[CreateAssetMenu(fileName = "invasion_", menuName = "AetherTree/Contenu/InvasionData")]
public class InvasionData : WorldEventData
{
    [Header("Variantes possibles (ex: élémentaires)")]
    public List<InvasionVariant> possibleVariants = new List<InvasionVariant>();

    [Header("Zone de spawn autour du point d'ancrage")]
    [Tooltip("Rayon (mètres) autour du point d'ancrage tiré au hasard — jamais toute la map.")]
    public float spawnRadius = 20f;

    [Header("Cadence")]
    [Tooltip("Délai entre 2 vagues ET entre 2 vagues de renfort (secondes).")]
    public float waveInterval = 90f;

    public override string DisplayName => "Invasion";

    private readonly List<Mob> _aliveMobs = new List<Mob>();
    private bool _bossDead;

    protected override bool OwnsMob(Entity target) => target is Mob m && _aliveMobs.Contains(m);

    public override IEnumerator RunEvent(WorldEventScheduler scheduler)
    {
        if (possibleVariants == null || possibleVariants.Count == 0 || eligibleMaps == null || eligibleMaps.Count == 0)
        {
            Debug.LogWarning("[InvasionData] possibleVariants/eligibleMaps vide — cycle ignoré.");
            yield break;
        }

        WorldEventMapEntry targetMap = PickRandomMap();
        InvasionVariant    variant   = possibleVariants[Random.Range(0, possibleVariants.Count)];

        AnnoncePanel.Instance?.Announce(
            $"Une invasion ({variant.variantName}) menace le Palier {targetMap.palier} dans {scheduler.FirstWarningOffset / 60f:F0} minutes !");
        yield return new WaitForSeconds(Mathf.Max(0f, scheduler.FirstWarningOffset - scheduler.SecondWarningOffset));

        AnnoncePanel.Instance?.Announce(
            $"Une invasion ({variant.variantName}) menace le Palier {targetMap.palier} dans {scheduler.SecondWarningOffset / 60f:F0} minute(s) !");
        yield return new WaitForSeconds(scheduler.SecondWarningOffset);

        if (SceneManager.GetActiveScene().name != targetMap.sceneName) yield break;

        if (!WorldEventScheduler.TryGetRandomNavMeshPoint(out Vector3 anchor))
        {
            Debug.LogWarning("[InvasionData] Aucun NavMesh baké — événement annulé.");
            yield break;
        }

        AnnoncePanel.Instance?.Announce($"L'invasion ({variant.variantName}) a commencé sur le Palier {targetMap.palier} !");

        StartTracking();
        _aliveMobs.Clear();
        _bossDead = false;

        // ── 5 vagues fixes — pop sur le timer, n'attendent PAS que la précédente soit clear ──
        foreach (InvasionWave wave in variant.waves)
        {
            SpawnGroup(wave.mobs, anchor, targetMap.palier);
            yield return new WaitForSeconds(waveInterval);
        }

        // ── Boss — spawn une seule fois, après la dernière vague ──
        if (variant.boss != null && variant.boss.prefab != null)
        {
            GameObject bossObj = Instantiate(variant.boss.prefab, GetRandomPointInRadius(anchor), Quaternion.identity);
            Mob bossMob = bossObj.GetComponent<Mob>();
            bossMob?.InitializeSpawn(variant.boss, targetMap.palier);
            if (bossMob != null) _aliveMobs.Add(bossMob);
            AnnoncePanel.Instance?.Announce($"Le boss de l'invasion est apparu sur le Palier {targetMap.palier} !");
            bossMob?.OnDeath(() => _bossDead = true);
        }
        else
        {
            Debug.LogWarning("[InvasionData] Variante sans boss valide — pas de renforts, résolution immédiate dès que le reste est mort.");
            _bossDead = true; // évite un WaitUntil infini plus bas
        }

        // ── Renforts en boucle tant que le boss est vivant ──
        while (!_bossDead)
        {
            yield return new WaitForSeconds(waveInterval);
            if (_bossDead) break;
            SpawnGroup(variant.reinforcements, anchor, targetMap.palier);
        }

        // ── Boss mort — plus aucun spawn, attendre que tout le reste meure ──
        yield return new WaitUntil(() =>
        {
            _aliveMobs.RemoveAll(m => m == null || m.isDead);
            return _aliveMobs.Count == 0;
        });

        StopTracking();
        AnnoncePanel.Instance?.Announce($"L'invasion ({variant.variantName}) du Palier {targetMap.palier} a été repoussée !");
        GrantEventRewards(variant.rewardTable);
    }

    private void SpawnGroup(List<InvasionMobEntry> mobs, Vector3 anchor, int palier)
    {
        if (mobs == null) return;
        foreach (var entry in mobs)
        {
            if (entry.mob == null || entry.mob.prefab == null) continue;
            for (int i = 0; i < entry.count; i++)
            {
                GameObject obj = Instantiate(entry.mob.prefab, GetRandomPointInRadius(anchor), Quaternion.identity);
                Mob mob = obj.GetComponent<Mob>();
                mob?.InitializeSpawn(entry.mob, palier);
                if (mob != null) _aliveMobs.Add(mob);
            }
        }
    }

    private Vector3 GetRandomPointInRadius(Vector3 center)
    {
        Vector2 offset = Random.insideUnitCircle * spawnRadius;
        Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);
        if (NavMesh.SamplePosition(candidate, out var hit, spawnRadius, NavMesh.AllAreas))
            return hit.position;
        return center; // repli sur l'ancre si le sample échoue — dégradation gracieuse
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (possibleVariants != null)
        {
            foreach (var variant in possibleVariants)
            {
                if (variant.boss != null &&
                    (variant.boss.mobType != MobType.EventInvasionMob || variant.boss.invasionRole != InvasionRole.Boss))
                    Debug.LogWarning($"[InvasionData] {name} : {variant.boss.mobName} a mobType = " +
                        $"{variant.boss.mobType}/invasionRole = {variant.boss.invasionRole}, attendu " +
                        "EventInvasionMob + InvasionRole.Boss pour un boss d'invasion.");

                if (variant.waves != null)
                    foreach (var wave in variant.waves)
                        WarnIfNotInvasionTrash(wave.mobs);

                WarnIfNotInvasionTrash(variant.reinforcements);
            }
        }

        SyncEligibleMapsSceneNames();
    }

    private void WarnIfNotInvasionTrash(List<InvasionMobEntry> entries)
    {
        if (entries == null) return;
        foreach (var entry in entries)
            if (entry.mob != null &&
                (entry.mob.mobType != MobType.EventInvasionMob || entry.mob.invasionRole != InvasionRole.Normal))
                Debug.LogWarning($"[InvasionData] {name} : {entry.mob.mobName} a mobType = " +
                    $"{entry.mob.mobType}/invasionRole = {entry.mob.invasionRole}, attendu " +
                    "EventInvasionMob + InvasionRole.Normal pour un mob de vague/renfort.");
    }
#endif
}
