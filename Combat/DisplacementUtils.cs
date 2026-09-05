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
    /// Clamp la destination sur le NavMesh (SamplePosition) avant le Warp — sans ça,
    /// un recul/pull vers un point hors mesh (mur, bord) fait échouer Warp() en silence
    /// et l'entité ne bouge pas du tout. Si AUCUN point valide n'est trouvé dans le rayon
    /// (destination complètement hors mesh — sous l'eau, hors niveau...), on abandonne le
    /// déplacement plutôt que d'utiliser la destination brute — sans ça, une entité sans
    /// NavMeshAgent (ou agent désactivé) se retrouverait téléportée directement hors-mesh
    /// via transform.position, sans aucun filet de sécurité.
    /// </summary>
    public static void WarpToNavMesh(Entity entity, Vector3 destination)
    {
        if (entity == null) return;

        if (!UnityEngine.AI.NavMesh.SamplePosition(destination, out var hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
            return;
        destination = hit.position;

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
