using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================
// WORLDBOSSDATA.CS — Configuration de l'événement Boss Géant
// Path : Assets/Scripts/Data/Content/WorldBossData.cs
// Spec : docs/superpowers/specs/2026-09-30-world-event-generic-invasion-design.md
//
// Hérite de WorldEventData (Data/Content/WorldEventData.cs) — le timer/les offsets d'annonce
// vivent sur WorldEventScheduler (partagés par tous les types), pas ici. Chaque WorldBossEntry
// porte SA PROPRE récompense (rewardTable) : deux boss différents dans possibleBosses n'ont
// jamais la même récompense d'événement (Florian, 2026-09-30).
// =============================================================

[System.Serializable]
public class WorldBossEntry
{
    public MobData   boss;
    public LootTable rewardTable;
}

[CreateAssetMenu(fileName = "wboss_", menuName = "AetherTree/Contenu/WorldBossData")]
public class WorldBossData : WorldEventData
{
    [Header("Boss possibles")]
    [Tooltip("Un boss est tiré au hasard parmi ceux-ci à chaque événement Boss Géant. Chaque " +
             "entrée porte sa propre récompense d'événement (rewardTable) — jamais partagée " +
             "entre plusieurs boss. Devrait avoir MobType = EventWorldBoss (juste un avertissement " +
             "si un autre type est glissé ici, pas un blocage).")]
    public List<WorldBossEntry> possibleBosses = new List<WorldBossEntry>();

    [Header("Résolution")]
    [Tooltip("Si le boss n'est pas tué dans ce délai après son spawn, il despawn.")]
    public float despawnTimeout = 1800f; // 30 min

    public override string DisplayName => "Boss Géant";

    private GameObject _aliveBossObj;
    private Mob         _aliveMob;
    private bool        _resolved;
    private bool        _killedFlag;

    protected override bool OwnsMob(Entity target)
        => target != null && ReferenceEquals(target, _aliveMob);

    public override IEnumerator RunEvent(WorldEventScheduler scheduler)
    {
        if (possibleBosses == null || possibleBosses.Count == 0 || eligibleMaps == null || eligibleMaps.Count == 0)
        {
            Debug.LogWarning("[WorldBossData] possibleBosses/eligibleMaps vide — cycle ignoré.");
            yield break;
        }

        WorldEventMapEntry targetMap = PickRandomMap();
        WorldBossEntry     entry     = possibleBosses[Random.Range(0, possibleBosses.Count)];

        AnnoncePanel.Instance?.Announce(
            $"Un {DisplayName} menace le Palier {targetMap.palier} dans {scheduler.FirstWarningOffset / 60f:F0} minutes !");
        yield return new WaitForSeconds(Mathf.Max(0f, scheduler.FirstWarningOffset - scheduler.SecondWarningOffset));

        AnnoncePanel.Instance?.Announce(
            $"Un {DisplayName} menace le Palier {targetMap.palier} dans {scheduler.SecondWarningOffset / 60f:F0} minute(s) !");
        yield return new WaitForSeconds(scheduler.SecondWarningOffset);

        if (SceneManager.GetActiveScene().name != targetMap.sceneName) yield break; // personne présent

        if (entry.boss == null || entry.boss.prefab == null)
        {
            Debug.LogWarning($"[WorldBossData] Boss sans prefab (entrée tirée) — événement annulé.");
            yield break;
        }
        if (!WorldEventScheduler.TryGetRandomNavMeshPoint(out Vector3 spawnPos))
        {
            Debug.LogWarning("[WorldBossData] Aucun NavMesh baké — événement annulé.");
            yield break;
        }

        _aliveBossObj = Instantiate(entry.boss.prefab, spawnPos, Quaternion.identity);
        _aliveMob     = _aliveBossObj.GetComponent<Mob>();
        _aliveMob?.InitializeSpawn(entry.boss, targetMap.palier);

        AnnoncePanel.Instance?.Announce($"Le {DisplayName} est apparu sur le Palier {targetMap.palier} !");

        StartTracking();
        _resolved   = false;
        _killedFlag = false;
        _aliveMob?.OnDeath(() => MarkResolved(killed: true));
        Coroutine timeoutCoroutine = scheduler.StartCoroutine(TimeoutAfterDelay(despawnTimeout));

        yield return new WaitUntil(() => _resolved);

        // StopTracking()/GrantEventRewards() APRÈS le WaitUntil, jamais dans le callback de mort
        // lui-même — Mob.Die() (synchrone, déclenché par TakeDamage) appelle onDeathCallback AVANT
        // que SkillSystem, dans son appelant, publie le DamageDealtEvent de CE MÊME coup. Résoudre
        // directement dans le callback désabonnait le tracking avant que le coup fatal soit
        // compté — le joueur qui achève le boss pouvait être exclu si ce coup le faisait franchir
        // minHitsToBeEligible. En ne faisant que positionner _resolved/_killedFlag dans le
        // callback, la résolution réelle n'arrive qu'à la reprise de la coroutine (frame suivante
        // au plus tôt) — le DamageDealtEvent du coup fatal est alors déjà traité.
        StopTracking();
        if (timeoutCoroutine != null) scheduler.StopCoroutine(timeoutCoroutine);

        if (_killedFlag)
        {
            AnnoncePanel.Instance?.Announce($"Le {DisplayName} du Palier {targetMap.palier} a été vaincu !");
            GrantEventRewards(entry.rewardTable);
        }
        else
        {
            if (_aliveBossObj != null) Destroy(_aliveBossObj);
            AnnoncePanel.Instance?.Announce($"Le {DisplayName} du Palier {targetMap.palier} s'est retiré...");
        }

        _aliveBossObj = null;
        _aliveMob     = null;
    }

    private IEnumerator TimeoutAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        MarkResolved(killed: false);
    }

    /// <summary>Positionne le résultat — mort ET timeout y passent tous les deux. Le garde
    /// _resolved empêche une double résolution si les deux se déclenchent presque en même temps.
    /// Ne fait QUE marquer l'état : la résolution réelle (StopTracking/GrantEventRewards) vit dans
    /// RunEvent, après le WaitUntil — jamais ici.</summary>
    private void MarkResolved(bool killed)
    {
        if (_resolved) return;
        _resolved   = true;
        _killedFlag = killed;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (possibleBosses != null)
            foreach (var entry in possibleBosses)
                if (entry.boss != null && entry.boss.mobType != MobType.EventWorldBoss)
                    Debug.LogWarning($"[WorldBossData] {name} : {entry.boss.mobName} a mobType = " +
                        $"{entry.boss.mobType}, attendu EventWorldBoss pour un Boss Géant.");

        SyncEligibleMapsSceneNames();
    }
#endif
}
