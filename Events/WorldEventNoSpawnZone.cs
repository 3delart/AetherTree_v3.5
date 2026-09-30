using UnityEngine;

// =============================================================
// WORLDEVENTNOSPAWNZONE.CS — Zone interdite aux events mondiaux (villes, zones sûres)
// Path : Assets/Scripts/Events/WorldEventNoSpawnZone.cs
//
// Posé à la main dans chaque ville — WorldEventScheduler.TryGetRandomNavMeshPoint reroll tant
// qu'un point tiré tombe dans le rayon d'une zone active de la scène courante. Empêche un World
// Boss/une Invasion de spawn au milieu des PNJ, qui prendraient les coups à leur place (Florian,
// 2026-09-30). Couvre le point d'ancrage/spawn de l'event (TryGetRandomNavMeshPoint) — pas les
// positions individuelles des mobs à l'intérieur de la zone d'Invasion (spawnRadius), qui restent
// centrées sur cet ancrage déjà hors ville.
// =============================================================
public class WorldEventNoSpawnZone : MonoBehaviour
{
    [Tooltip("Rayon (mètres) autour de ce transform à exclure du tirage de point d'event mondial.")]
    public float radius = 30f;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.35f);
        Gizmos.DrawSphere(transform.position, radius);
    }
#endif
}
