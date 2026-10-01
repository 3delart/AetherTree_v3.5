using UnityEngine;

// =============================================================
// GROUNDRAYCASTUTIL.CS — Raycast écran→sol partagé, ignore les colliders de mob/PNJ sur le trajet
// Path : Assets/Scripts/Utils/GroundRaycastUtil.cs
//
// Un Physics.Raycast classique prend le PREMIER collider touché — un mob au collider large
// (World Boss/Invasion) entre la caméra et le sol intercepte le rayon avant d'atteindre le sol
// réel, donnant soit un point faux (sur le mob), soit aucun résultat du tout si le code attend
// explicitement le tag "Ground". RaycastAll + on prend le plus proche tagué "Ground" (Florian,
// 2026-10-01 — trouvé sur le clic-déplacement (PlayerController.cs), puis signalé pareil sur les
// skills GroundTarget (SkillBar.cs/TargetingSystem.cs) : 3 endroits, même bug, même fix).
// =============================================================
public static class GroundRaycastUtil
{
    public static bool TryRaycastGround(Ray ray, out RaycastHit groundHit, float maxDistance = Mathf.Infinity)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        groundHit = default;
        if (hits.Length == 0) return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit candidate in hits)
        {
            if (!candidate.collider.CompareTag("Ground")) continue;
            groundHit = candidate;
            return true;
        }
        return false;
    }
}
