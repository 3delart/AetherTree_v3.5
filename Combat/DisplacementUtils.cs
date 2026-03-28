using UnityEngine;

// =============================================================
// DISPLACEMENTUTILS.CS — Helpers de déplacement partagés
// Path : Assets/Scripts/Systems/DisplacementUtils.cs
// AetherTree GDD v3.5
//
// Utilisé par SkillSystem (joueur) ET MobSkillSystem (mobs).
// Centralise WarpToNavMesh, WarpEntity, ApplyDisplacementAoE
// pour éviter la duplication.
// =============================================================

public static class DisplacementUtils
{
    /// <summary>
    /// Warp NavMesh-safe : utilise Agent.Warp si disponible, sinon transform direct.
    /// </summary>
    public static void WarpToNavMesh(Entity entity, Vector3 destination)
    {
        if (entity == null) return;
        var agent = entity.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.Warp(destination);
        else
            entity.transform.position = destination;
    }

    /// <summary>
    /// Déplace une entité vers ou loin d'un point d'origine.
    /// </summary>
    public static void WarpEntity(Entity entity, Vector3 origin, float force, bool towards)
    {
        if (entity == null) return;
        Vector3 dir  = (entity.transform.position - origin).normalized;
        if (towards) dir = -dir;
        Vector3 dest = entity.transform.position + dir * force;
        WarpToNavMesh(entity, dest);
    }

    /// <summary>
    /// Applique un déplacement (vers ou loin du centre) sur toutes les entités
    /// dans la zone, en excluant le caster.
    /// Retourne le nombre d'entités touchées.
    /// </summary>
    public static int ApplyDisplacementAoE(
        Vector3 center,
        float   aoeRadius,
        float   force,
        bool    towardsCenter,
        Entity  caster,
        int     layerMask = ~0)
    {
        Collider[] hits = Physics.OverlapSphere(center, aoeRadius, layerMask);
        int count = 0;
        foreach (Collider col in hits)
        {
            Entity entity = col.GetComponentInParent<Entity>();
            if (entity == null || entity == caster || entity.isDead) continue;
            WarpEntity(entity, center, force, towards: towardsCenter);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Regroupe toutes les entités de la zone sur le point central.
    /// Retourne le nombre d'entités touchées.
    /// </summary>
    public static int GatherAoE(Vector3 center, float aoeRadius, Entity caster, int layerMask = ~0)
    {
        Collider[] hits = Physics.OverlapSphere(center, aoeRadius, layerMask);
        int count = 0;
        foreach (Collider col in hits)
        {
            Entity entity = col.GetComponentInParent<Entity>();
            if (entity == null || entity == caster || entity.isDead) continue;
            WarpToNavMesh(entity, center);
            count++;
        }
        return count;
    }
}
